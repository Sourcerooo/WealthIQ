# WealthIQ Phase 2 — Data Administration & Vorabpauschale Correction (Design)

- **Date:** 2026-05-31
- **Status:** Accepted (Design), ready for implementation plan
- **Branch:** `feature/Phase2`
- **Context:** Phase 2 of the WealthIQ v1 rebuild. Phase 1 (foundation/persistence), the import→persist pipeline, and tax-replay + dashboard are all implemented. This phase (a) makes all data the tax engine depends on **administrable from inside the app** — clearable, reloadable, and fetchable directly from the internet, replacing the two standalone Python scripts — and (b) **corrects several confirmed defects in the Vorabpauschale calculation** (most importantly the multi-year start-value bug), which the new data layer makes possible. A new "Data Administration" UI exposes every operation.

> **Independent review:** an external cross-check (`GPT_vorabpauschale_analysis.md`) validated the multi-year bug and surfaced four further §18 InvStG issues. All are folded into this revision. The Vorabpauschale algorithm in §6 was additionally reconciled against the **verbatim statute** (see §6.5 for quotes + sources).

---

## 1. Goals & Non-Goals

**Goals (priority order)**
1. **[HIGHEST PRIORITY] Make the Vorabpauschale calculation §18-InvStG-correct** (§6). Specifically: re-base every holding year to that year's redemption price (year-start → year-end), add distributions into the statutory cap, apply the 1/12 acquisition-month reduction to the final Vorabpauschale, restrict Vorabpauschale to investment funds, and fail-fast on missing Basiszins/classification. By the end of Phase 2 these are correctly and verifiably fixed.
2. Store full daily historical prices in SQLite (the data prerequisite for Goal 1), currency/exchange-aware, replacing `scripts/download_price_history.py` with a native C# Yahoo Finance integration that refreshes incrementally.
3. Derive **year-end *and* year-start** prices from stored historical prices (drop the dedicated `YearEndPrice` table).
4. Replace `scripts/download_fx_rates.py` with a native C# ECB integration storing rates in SQLite.
5. Fetch the official Basiszins for the Vorabpauschale from an internet source (BMF), with a manual override.
6. Clear the imported ledger for a clean reload from scratch.
7. Clear and reload every seeded reference dataset (Basiszins, FX rates, historical prices, instruments).
8. View / edit / delete / upload instrument reference data (incl. fund classification + listings).
9. A single "Data Administration" UI page exposing all of the above.
10. All external data sources sit behind interfaces so providers can be swapped later.

**Non-Goals (unchanged from v1)**
- Portfolio valuation / charts, PDF export, additional brokers, strategies/backtesting, multi-base-currency.
- The "Vorabpauschale for a position held beyond the last ledger entry" as-of/through-year parameter remains a known thin spot and is **out of scope** here (the calculator still replays only up to the last ledger entry year).

---

## 2. Locked decisions (this phase)

| Topic | Decision | Rationale |
|---|---|---|
| **Multi-year Vorabpauschale** | **Fix within Phase 2 (highest priority).** | Confirmed correctness bug affecting the *normal* buy-and-hold case; the project's purpose is a Finanzamt-grade report |
| **Start value — all years** | **Year-start redemption price** (first valuation day of the year), uniformly, **including the acquisition year** (option b) | Closest to the literal statute (§6.5). Acquisition cost is no longer used for Vorabpauschale — only for the realized gain at sale |
| **Statutory cap** | `max(0, (endValue − startValue) + distributions)` — distributions **added** to the cap | §18(1): *"…Rücknahmepreis zuzüglich der Ausschüttungen"* (§6.5) |
| **1/12 acquisition-month reduction** | Applied to the **final** per-share Vorabpauschale, not to the Basisertrag | §18(2): *"vermindert sich **die Vorabpauschale** um ein Zwölftel…"* (§6.5) |
| **Vorabpauschale applicability** | Only for instruments **explicitly classified** as investment funds (`SubjectToVorabpauschale = true`). No inference | §18 applies to Investmentfonds, not ordinary stocks; fail-fast over assumptions |
| **No silent defaults** | Missing instrument classification/profile, or missing Basiszins for a year in replay scope → **blocking error**. The 30% Teilfreistellung default is **removed** | User directive: fail-fast, force data correction, no assumptions |
| **Basiszins contract** | `IBasisInterestRateProvider` returns **nullable**: `null` = missing (blocking error in scope), `≤0` = official zero/negative (skip year, no price lookup) | Distinguish missing data from an official non-positive rate |
| Implementation sequencing | **Two stages**: (A) data layer + accessors with the year-end path proven behavior-preserving; (B) corrected formula as the capstone | Isolates plumbing risk from deliberate formula change |
| Yahoo acquisition | **Thin `HttpClient`**, no third-party NuGet; port the proven Python v8 chart call | All libraries wrap the same unofficial endpoint; several have an EU cookie/consent bug; the Python call already works from the EU; full control, hidden behind an interface |
| Yahoo politeness | One symbol at a time, fixed inter-request delay, exponential back-off + bounded retry on 429/5xx | Avoid throttling/blocking |
| Yahoo caching | Incremental: fetch only `(maxStoredDate+1 … today)` per symbol; immutable older bars never re-requested; explicit "Force full reload" wipes+refetches one symbol | Downloaded bars rarely change; minimize requests |
| Basiszins source | **Scrape the official BMF published value** behind `IBasisInterestRateSource`; manual override always available | One authoritative number per year; matches Finanzamt expectations |
| Seed files | **Keep committed CSV/JSON as offline bootstrap seed** (first run + CI/regression fixtures). Internet refresh writes to the DB only, never back to files | Keeps CI deterministic; clean separation of seed vs. live data |
| Multi-listing | **Design for safety**: support multiple listings per ISIN keyed by `(Isin, Currency)`; never mix currencies | User may hold the same ISIN in multiple currencies/exchanges |
| Price source for tax | **Derived** from `HistoricalPrice`; each redemption price FX-converted at its **own bar date** (`Close`, not `AdjustedClose`); `YearEndPrice` table removed | Single source of truth; honors the "convert at the event's own time" FX rule; distributions are handled separately so adjusted-close would double-count |
| Posting date | Vorabpauschale posted on **1 Jan of Y+1** as a documented simplification | §18(3) deems inflow on the first working day of Y+1; only the tax *year* is material |

---

## 3. Architecture: Provider → Store → Refresh

For each externally-sourced dataset, three concerns are separated so providers are swappable and the DB remains the single source of truth. The Domain and tax engine never touch the internet.

| Concern | Lives in | Talks to | New/changed types |
|---|---|---|---|
| **Source/Provider** (fetch) | `Application` interface, `Infrastructure` adapter | the internet | `IHistoricalPriceProvider` → `YahooHistoricalPriceProvider`; `IFxRateProvider` → `EcbFxRateProvider`; `IBasisInterestRateSource` → `BmfBasisInterestRateSource` |
| **Lookup/Store** (read DB) | `Application` interface, `Infrastructure` adapter | SQLite | `IHistoricalPriceLookup` → **`DbHistoricalPriceLookup`** (new, replaces `CsvHistoricalPriceLookup`); `IFxRateLookup` → `DbFxRateLookup` (exists); `IInstrumentMarketDataMap` → **`DbInstrumentMarketDataMap`** (new, replaces `JsonInstrumentMarketDataMap`); `IBasisInterestRateProvider` → `DbBasisInterestRateProvider` (exists, contract change) |
| **Refresh service** (orchestrate) | `Application` | provider + store | one service per dataset; fetches, dedups/caches, upserts, returns a structured result (`Added/Updated/Skipped` + `ImportDiagnostic` list) |

Dependency direction is preserved: providers/stores live in `Infrastructure`, interfaces in `Application`, and only `Web` wires them.

### Fail-fast & diagnostics
Refresh operations follow the existing import philosophy: collect structured diagnostics (`Info/Warning/Error/Fatal`), and abort the dataset's transaction if any blocking diagnostic occurs — no silent drops. A fetched bar whose reported currency ≠ the configured listing currency is a **blocking error**.

---

## 4. Data model changes

### New table — `HistoricalPrice` (replaces `historical_prices.csv`)
- **Key:** `(ProviderSymbol, Date)`
- **Columns:** `ProviderSymbol` (string), `Date` (DateOnly), `Currency` (string), `Open`, `High`, `Low`, `Close`, `AdjustedClose` (decimal), `Volume` (long)
- Currency is intrinsic to the listing; distinct symbols (`VUSA.L` GBP vs `CSPX.AS` EUR) keep currencies from mixing. The tax engine uses **`Close`** (not `AdjustedClose`).

### New table — `InstrumentListing` (replaces `market_data_mappings.json`, multi-currency-safe)
- **Key:** `(Isin, Currency)` — enables the same ISIN in EUR *and* GBP without mixing
- **Columns:** `Isin`, `Currency`, `Provider`, `ProviderSymbol`, `Exchange` (nullable), `Notes` (nullable)
- A lot resolves `(ISIN, lot currency)` → `ProviderSymbol`. A missing listing for a held `(ISIN, currency)` is a blocking error at tax replay.

### Change — `InstrumentProfile`
- `Isin`, `Name`, `Teilfreistellungsquote`; **add** `Type` (string, mirrors `instruments.json`'s `type`) and **`SubjectToVorabpauschale`** (bool). Editable in UI.
- **No defaults:** an instrument held over a year-end with no profile row → blocking error. The previous 30% Teilfreistellung / `"Auto-Generated"` fallback in `DbInstrumentProfileEnricher` / `JsonInstrumentProfileEnricher` is **removed**.
- The committed `instruments.json` seed is extended so each instrument explicitly carries `type`, `tfs_quote`, and `subject_to_vorabpauschale`.

### Change — `FxRate` `(Date, Currency) → RateToEur`
- Now also written by the ECB refresh, in addition to file seeding.

### Change — `BasisInterestRate` `(Year) → Rate`
- Now also written by the BMF refresh and manual edits. Provider contract becomes nullable (§5.3).

### Remove — `YearEndPrice` table
- Year-end **and** year-start prices become derived from `HistoricalPrice` (see §5.4 and §6). The migration drops the table; `DbYearEndPriceProvider` is replaced by a derived provider.

### New small table — `DataRefreshLog` `(Dataset, LastRefreshedUtc, Note)`
- Powers the admin page's "last refreshed" status. One row per dataset, upserted on each refresh.

A single EF Core migration adds `HistoricalPrice`, `InstrumentListing`, `DataRefreshLog`, the `InstrumentProfile.Type` + `InstrumentProfile.SubjectToVorabpauschale` columns, and drops `YearEndPrice`.

---

## 5. External providers & price accessors

### 5.1 Yahoo historical prices — `YahooHistoricalPriceProvider : IHistoricalPriceProvider`
- Ports `download_price_history.py`: `GET https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?period1=…&period2=…&interval=1d&events=history` with a browser `User-Agent`, parses the `chart.result[0]` payload (timestamps + `indicators.quote[0]` OHLCV + `adjclose` + `meta.currency`), skips incomplete rows.
- **Interface:** `Task<HistoricalPriceFetchResult> FetchAsync(string providerSymbol, DateOnly from, DateOnly to, CancellationToken)` returning bars + the reported currency.
- **Politeness:** sequential per symbol; configurable inter-request delay; exponential back-off + bounded retries on 429/5xx; clear diagnostic on exhaustion.
- Registered via `IHttpClientFactory`.

### 5.2 ECB FX rates — `EcbFxRateProvider : IFxRateProvider`
- Ports `download_fx_rates.py`: fetches `https://www.ecb.europa.eu/stats/eurofxref/eurofxref-hist.xml`, parses daily cubes, emits `EUR=1.0` plus `currency_to_eur = 1/rate` for the supported currencies (USD/GBP/CHF, configurable), within a date window.
- **Interface:** `Task<IReadOnlyList<FxRateRecord>> FetchAsync(DateOnly from, DateOnly to, CancellationToken)`.
- **Coverage note:** the calculator converts each redemption price at its **own bar date**, which is a trading day and therefore an ECB business day, so a rate is normally present. The existing `NextAvailableOnOrAfter` roll-forward remains; a genuinely missing rate is a blocking error.

### 5.3 Basiszins — `BmfBasisInterestRateSource : IBasisInterestRateSource` + nullable provider
- Fetches the official BMF "Basiszins zur Berechnung der Vorabpauschale" value for a year (e.g. 2.53% for 2025, 3.20% for 2026) → `(year, rate)`. Interface: `Task<BasisInterestRateRecord?> FetchAsync(int year, CancellationToken)`. Defensive parsing; failure → diagnostic; manual override is the fallback.
- **Contract change:** `IBasisInterestRateProvider.GetRate(int year)` → returns `decimal?`. `DbBasisInterestRateProvider` returns `null` for an absent row (instead of `0`). The calculator interprets:
  - `null` for a year in replay scope `[firstYear, lastYear]` → **blocking error** (data gap; user must add the rate).
  - `≤ 0` → official zero/negative rate → **skip that year's Vorabpauschale entirely, before any price lookup** (§6.4).
  - `> 0` → compute.

### 5.4 Price accessors (the data prerequisite for §6)
- **`DbHistoricalPriceLookup : IHistoricalPriceLookup`** — reads `HistoricalPrice` by `ProviderSymbol`. Extend `PriceLookupDateHandling` with **`EarliestOnOrAfter`** (year-start lookups) alongside `ExactDate` and `LatestOnOrBefore`.
- **`DbInstrumentMarketDataMap : IInstrumentMarketDataMap`** — resolves `(ISIN, Currency)` → `ProviderSymbol` from `InstrumentListing`. Missing mapping is a blocking error.
- **New tax-facing accessor — `IInstrumentPriceProvider`** (Application, `Tax` namespace), implemented by **`DerivedInstrumentPriceProvider`** (Infrastructure):
  ```csharp
  public readonly record struct InstrumentQuote(decimal Close, Currency Currency, DateOnly AsOf);

  public interface IInstrumentPriceProvider
  {
      // Returns the redemption price (Close) for the instrument's listing in `currency`, resolved by date handling.
      // Close is in `Currency`; the CALLER converts to EUR. Null only when no listing/bar exists; the calculator
      // turns null into a blocking error.
      InstrumentQuote? GetQuote(string isin, Currency currency, DateOnly pricingDate, PriceQuoteHandling handling);
  }

  public enum PriceQuoteHandling { LatestOnOrBefore, EarliestOnOrAfter, ExactDate }
  ```
  `DerivedInstrumentPriceProvider` resolves the symbol via `IInstrumentMarketDataMap`, reads the bar via `IHistoricalPriceLookup`, returns `(close, barCurrency, barDate)`, and asserts `barCurrency == currency` (else blocking error). **It does not do FX** — conversion stays in the calculator. Replaces `IYearEndPriceProvider` / `DbYearEndPriceProvider` entirely.

---

## 6. The Vorabpauschale correction (core deliverable)

### 6.1 Current (buggy) behavior — for reference
`GermanTaxCalculator.PerformYearEndClosing` today, for **every** holding year, uses the lot's **original acquisition cost** as the base (`basisYield = acqCost × Basiszins × 0.7 × months/12`; `appreciation = max(0, yearEndPrice − acqCost)`), gates only on `instrument.ISIN` being non-empty, treats a missing Basiszins as `0` (silent skip), omits distributions from the cap, and applies `months/12` to the Basisertrag. Each of these is corrected below.

### 6.2 Corrected algorithm (uniform, §18 InvStG)

Run only when `Basiszins(Y) > 0` (else skip year — §6.4) and only for lots whose instrument has `SubjectToVorabpauschale = true`. For each open **long** lot with `RemainingQuantity > 0` held at the end of calendar year **Y**:

```
lotCurrency = lot.OpenUnitPrice.Currency

# --- per-share START and END redemption price, in EUR (UNIFORM across all years incl. acquisition year) ---
s = priceProvider.GetQuote(isin, lotCurrency, Jan 1 of Y, EarliestOnOrAfter)   # first valuation day of Y
e = priceProvider.GetQuote(isin, lotCurrency, Dec 31 of Y, LatestOnOrBefore)   # last  valuation day of Y
if s is null or e is null: BLOCKING ERROR
startValueEur = fxConverter.Convert(Money(s.Close, s.Currency), s.AsOf).Amount   # FX @ year-start bar date
endValueEur   = fxConverter.Convert(Money(e.Close, e.Currency), e.AsOf).Amount   # FX @ year-end  bar date

# --- distributions on THIS lot in Y (per share, EUR), paid on/after the lot's open date ---
distPerShare  = Σ distributions(Year==Y, AccountId, InstrumentId, Date >= lot.OpenTradeDate).PerShare

# --- Vorabpauschale (per share) ---
basisErtrag   = startValueEur × Basiszins(Y) × 0.7
cap           = max(0, (endValueEur − startValueEur) + distPerShare)     # §18(1): Mehrbetrag + Ausschüttungen
cappedBE      = min(basisErtrag, cap)
vorabFull     = max(0, cappedBE − distPerShare)                          # §18(1): Basisertrag übersteigt Ausschüttungen
monthFactor   = (lot.OpenTradeDate.Year == Y) ? (13 − lot.OpenTradeDate.Month) / 12 : 1   # §18(2)
vorabPerShare = vorabFull × monthFactor
if vorabPerShare <= 0: skip lot

totalVorab    = vorabPerShare × lot.RemainingQuantity
# accumulate raw totalVorab on the lot (AccumulatedVorabpauschale) and post the ledger entry,
# Teilfreistellung applied to the taxable amount, posted to (Y+1, 1 Jan) — UNCHANGED from today.
```

**Consequences of going uniform (option b):**
- Acquisition cost is **no longer used** in the Vorabpauschale at all — only in the realized-gain calculation at sale (`ConvertCostBasisToEur`, unchanged). This removes the prior fee-inclusion inconsistency.
- `EarliestOnOrAfter(Jan 1)` naturally returns the *first redemption price set in the year* — correct both for established funds (≈ 2 Jan) and for a fund launched mid-year (its launch-day price), exactly matching *"erster im Kalenderjahr festgesetzter Rücknahmepreis"*.
- The `months/12` factor scales only the **final** Vorabpauschale.

### 6.3 What stays unchanged
Lot/FIFO handling, short-position handling, the per-lot `AccumulatedVorabpauschale`, deduction of previously-taxed (full, raw) Vorabpauschale at sale per §19(1), Teilfreistellung application, and posting to year+1. Per-component FX (convert each amount at its own date) is retained — consistent with the realized-gain methodology and the project FX rule.

### 6.4 Ordering & edge cases (all fail-fast, no silent fallback)
1. Resolve `Basiszins(Y)`: `null` (in `[firstYear,lastYear]`) → **blocking error**; `≤0` → **skip year before any quote lookup**; `>0` → continue.
2. For each long lot: skip if `SubjectToVorabpauschale == false`; **blocking error** if the instrument has no profile/classification at all.
3. No `InstrumentListing` for `(ISIN, lotCurrency)` → blocking error (names ISIN + currency).
4. No year-start bar on/after 1 Jan, or no year-end bar on/before 31 Dec → blocking error.
5. Missing FX at a bar date → blocking error (existing `FxConverter`).
6. `bar.Currency != lotCurrency` → blocking error (mis-mapped listing).
7. `endValue ≤ startValue` and no distributions → cap 0 → no Vorabpauschale.
8. Partial-sold lot: value `RemainingQuantity`; acquisition year determined by `lot.OpenTradeDate.Year`.

### 6.5 Legal basis & decision record (why option b, distributions-in-cap, 1/12 placement)

Verbatim **§18 InvStG** (Vorabpauschale), Absätze 1–3, per the cited sources (key clauses matched identically across two independent retrievals; the official portal is captcha-walled):

> **(1)** *„Die Vorabpauschale ist der Betrag, um den die Ausschüttungen eines Investmentfonds innerhalb eines Kalenderjahres den Basisertrag für dieses Kalenderjahr unterschreiten. Der Basisertrag wird ermittelt durch Multiplikation des Rücknahmepreises des Investmentanteils **zu Beginn des Kalenderjahres** mit 70 Prozent des Basiszinses nach Absatz 4. Der Basisertrag ist auf den Mehrbetrag begrenzt, der sich zwischen dem **ersten und dem letzten im Kalenderjahr festgesetzten Rücknahmepreis zuzüglich der Ausschüttungen** innerhalb des Kalenderjahres ergibt. Wird kein Rücknahmepreis festgesetzt, so tritt der Börsen- oder Marktpreis an die Stelle des Rücknahmepreises."*
> **(2)** *„Im Jahr des Erwerbs der Investmentanteile **vermindert sich die Vorabpauschale** um ein Zwölftel für jeden vollen Monat, der dem Monat des Erwerbs vorangeht."*
> **(3)** *„Die Vorabpauschale gilt am ersten Werktag des folgenden Kalenderjahres als zugeflossen."*

Note: the §18(1) S.1 wording (*"um den die Ausschüttungen … den Basisertrag … unterschreiten"*) is algebraically `Vorabpauschale = max(0, cappedBasisertrag − Ausschüttungen)` — matching §6.2.

Decisions derived, with rationale:

1. **Start value = redemption price "am Anfang des Kalenderjahres" for every year, including the acquisition year (option b).** The statute names the year-start price and contains **no** clause substituting the acquisition price; the only acquisition-year adjustment is the 1/12 reduction. We deliberately follow the statute literally to be as defensible as possible.
   - *Considered and rejected:* the common-practice simplification (e.g. [finanzfluss](https://www.finanzfluss.de/steuern/vorabpauschale/): *"Im Jahr des Kaufs wird zur Berechnung der Wertsteigerung der Kaufpreis … herangezogen"*) which uses the **purchase price** in the acquisition year. It is intuitive and widely used, but it is not in the statutory text. The definitive administrative interpretation lives in a BMF-Schreiben we could not retrieve (gesetze-im-internet is captcha-walled; the circular is not cleanly published). **If that circular is later found to mandate the purchase-price approach, only the acquisition-year start value changes — a localized edit.** The user's explicit choice is "as close to the law as possible" → option (b).
2. **Distributions are added into the cap** (*"…Rücknahmepreis zuzüglich der Ausschüttungen"*), then subtracted from the capped Basisertrag — the current code omitted them from the cap, understating Vorabpauschale when the cap binds and distributions exist.
3. **The 1/12 reduction applies to "die Vorabpauschale"** (final amount), not the Basisertrag — the current code reduced the Basisertrag, which differs when the cap binds or distributions exist.
4. **Cap bounds = first/last redemption price of the year** — exactly our `EarliestOnOrAfter(Jan 1)` / `LatestOnOrBefore(Dec 31)` derivation, independently validating the year-start choice.
5. **Posting on 1 Jan of Y+1** is a documented simplification of §18(3)'s "erster Werktag"; only the tax year is material and it is unaffected.

**Sources:** §18 InvStG — [gesetze-im-internet.de/invstg_2018/__18.html](https://www.gesetze-im-internet.de/invstg_2018/__18.html), verbatim copy at [juraforum.de](https://www.juraforum.de/gesetze/invstg/18-vorabpauschale); common-practice example — [finanzfluss.de](https://www.finanzfluss.de/steuern/vorabpauschale/). Basiszins values/source — BMF-Schreiben (e.g. 2026-01-13) and the Bundesbank term-structure series (see §5.3).

---

## 7. Staged delivery & regression strategy (de-risking the engine change)

**Stage A — data layer + accessors, behavior-preserving checkpoint.**
- Build `HistoricalPrice`, `InstrumentListing`, `DbHistoricalPriceLookup` (+ `EarliestOnOrAfter`), `DbInstrumentMarketDataMap`, `DerivedInstrumentPriceProvider`, the nullable Basiszins contract, and the Yahoo/ECB/BMF providers + refresh services.
- Wire the calculator to source the **year-end** price from `DerivedInstrumentPriceProvider` **while keeping the current (acquisition-cost) formula**.
- Engineer committed historical-price + FX fixtures under `data/test/configuration/` so the **derived EUR year-end equals the current `prices.csv` value to the cent**.
- **`GermanTaxRegressionTests` must pass with its existing expected values UNCHANGED.** Proves the new data path moved no number.

**Stage B — corrected formula, deliberate behavior change (the capstone).**
- Implement §6.2 in full (uniform year-start, distributions-in-cap, 1/12 on final Vorab, fund-gating, Basiszins fail-fast).
- **Recompute the regression baseline** per §8 and update `GermanTaxRegressionTests`, each changed figure commented with arithmetic and cause (per CLAUDE.md).
- Add the new targeted tests in §13. **Phase 2 is not complete until Stage B is done.**

---

## 8. Regression-baseline recomputation methodology (do this exactly)

The current `GermanTaxRegressionTests` asserts exact 2024 disposal + Vorabpauschale figures from `data/test`. Stage B changes Vorabpauschale for multi-year lots (year-start rebasing), for any year with distributions where the cap binds (distributions-in-cap), and possibly acquisition-year amounts (1/12 placement + year-start base). To regenerate without mistakes:

1. **Enumerate Vorabpauschale-bearing lots** from the test statements: `ISIN`, currency, `OpenTradeDate`, quantity (+ partial sales), per year up to 2024, and confirm each instrument's `SubjectToVorabpauschale`.
2. **Assemble fixtures** so that for every `(ISIN, currency, year)` a held *fund* lot needs, the committed `HistoricalPrice` fixture contains the **first** and **last** trading bar of the year (in the lot's currency) plus the **FX fixtures** for those two dates. Use deliberate, documented round values — they are the single source of truth for the expected numbers.
3. **Compute per lot, per year** with §6.2 exactly (note: acquisition year now also uses the year-start fixture price, with `monthFactor`).
4. **Apply distributions-in-cap** and the **1/12-on-final-Vorab** rules; multiply by `RemainingQuantity`; apply `(1 − tfs)` for the taxable amount.
5. **Sum** to the report figures the test asserts; record the arithmetic in a worked table in the implementation plan **and** as comments in the test.
6. **Independent witness:** a standalone test (`Vorabpauschale_MultiYearHold_RebasesToYearStart`) reproduces one lot's multi-year numbers from first principles.
7. **Sanity check** vs §6.1: rebasing typically *raises* Vorabpauschale for an appreciating multi-year hold; distributions-in-cap *raises* it where the cap binds.

> Note on `IE00B3XXRP09` (mapped to `VUSA.L` GBP; `prices.csv` stored EUR): fixtures must model the lot in its true `OpenUnitPrice.Currency` with matching-currency bars + FX. Note on `IE00B4ND3602` (gold ETC): seeded `SubjectToVorabpauschale = true` for now so its baseline does not move; flagged for a later tax determination by the user. Surfacing both is intended.

---

## 9. Instrument reference administration

The editable "Instrument" is the union of two normalized tables, presented as one entity in the UI:
- **Profile:** `Isin`, `Name`, `Type`, `Teilfreistellungsquote`, `SubjectToVorabpauschale`
- **Listings (0..n):** `Currency`, `ProviderSymbol`, `Provider`, `Exchange`, `Notes`

`IInstrumentReferenceAdmin` (Application service) provides:
- **List** all instruments with their listings.
- **Add / Edit** profile + listings; validation: ISIN format, `Teilfreistellungsquote ∈ [0,1]`, non-empty `ProviderSymbol` per listing, unique `(Isin, Currency)`, `SubjectToVorabpauschale` explicitly set.
- **Delete**: guarded — if the ISIN is referenced by ledger entries, warn before allowing deletion.
- **Upload**: accept the existing `instruments.json` (profiles, extended with `subject_to_vorabpauschale`) and `market_data_mappings.json` (listings) shapes; choice of **merge** or **replace**.

---

## 10. Clearing & reload semantics

- **Clear ledger**: transactional delete of `PortfolioEntries` + `ImportBatches` + `ImportDiagnostics` + `Accounts`, with an option to also purge raw audit files in `data/app/audit`. Reference/market data untouched. Double-confirm.
- **Per-dataset Clear**: transactional truncate of the dataset's table(s).
- **Repopulate** offers two paths per applicable dataset: **Re-seed from committed files** (offline bootstrap) and **Refresh from internet** (ECB / Yahoo / BMF, DB-only).
- Refresh services are idempotent: re-running merges/updates without duplicating (FX by `(Date,Currency)`, prices by `(Symbol,Date)`, Basiszins by `Year`).

---

## 11. Data Administration UI (`/data-admin`)

A single MudBlazor page, one collapsible card per dataset, each showing **status** and **actions**. Nav link added to `MainLayout`.

| Card | Status shown | Actions |
|---|---|---|
| **Ledger** | entries, accounts, batches | Clear ledger (± purge raw files) — double-confirm |
| **Historical prices** | symbols, per-symbol date range, last refreshed | Refresh (incremental), Force full reload (per symbol/all), Clear |
| **FX rates (ECB)** | currencies, date range, last refreshed | Refresh, Clear, Re-seed from file |
| **Basiszins (BMF)** | years present + rate, last refreshed | Refresh from BMF, Manual add/edit, Clear, Re-seed |
| **Instruments** | count | Table with inline edit/delete (incl. `SubjectToVorabpauschale`), Add, Upload (json, merge/replace) |

Long-running refreshes run asynchronously with progress and a result summary (added/updated/skipped + diagnostics), reusing the diagnostic-table style from the Import page.

---

## 12. DI / composition (`Web/Program.cs`)
- Register `IHttpClientFactory`; bind `YahooHistoricalPriceProvider`, `EcbFxRateProvider`, `BmfBasisInterestRateSource` to their interfaces.
- Repoint `IHistoricalPriceLookup` → `DbHistoricalPriceLookup`, `IInstrumentMarketDataMap` → `DbInstrumentMarketDataMap`; register `IInstrumentPriceProvider` → `DerivedInstrumentPriceProvider`; **remove** `IYearEndPriceProvider`.
- Register the per-dataset refresh services, clear services, and `IInstrumentReferenceAdmin`.
- Configuration knobs (delays, retry counts, supported FX currencies, source URLs) via `appsettings` bound options.

---

## 13. Testing

- **Stage A regression (must stay green, UNCHANGED):** `GermanTaxRegressionTests` with fixtures reproducing current `prices.csv` year-end EUR values.
- **Stage B regression (deliberately updated):** `GermanTaxRegressionTests` recomputed per §8, with arithmetic comments.
- **New corrected-calc tests:**
  - `Vorabpauschale_MultiYearHold_RebasesToYearStart` (the core fix; independent witness).
  - `Vorabpauschale_AcquisitionYear_UsesYearStartPriceAndProRatesFinalAmount` (option b + 1/12 on final).
  - `Vorabpauschale_DistributionIncludedInAppreciationCap_WhenCapBinds` (Finding 1).
  - `Vorabpauschale_OrdinaryStockWithIsin_IsSkipped` (fund-gating).
  - `Vorabpauschale_MissingClassification_ThrowsBlocking`.
  - `Vorabpauschale_MissingBasiszins_ThrowsBlocking` and `Vorabpauschale_NonPositiveBasiszins_DoesNotRequirePrices`.
  - `Vorabpauschale_NonEurLot_ConvertsYearStartAndYearEndAtOwnDates`.
  - Blocking-error cases: missing listing, missing year-start bar, missing year-end bar, missing FX, currency mismatch.
- **Derived price provider:** symbol resolution per currency, `EarliestOnOrAfter` / `LatestOnOrBefore` selection, currency-mismatch guard.
- **Refresh services:** incremental gap fetch, dedup/idempotency, currency-mismatch diagnostic, back-off path (provider mocked — **no live network**).
- **Providers:** parse committed sample Yahoo/ECB/BMF payloads; no live calls.
- **Instrument admin:** validation, delete-guard, upload merge/replace, `SubjectToVorabpauschale` round-trip.
- **Clearing:** ledger clear leaves reference data intact and vice-versa; transactions roll back on failure.

---

## 14. Work units (for the implementation plan; order matters)

*Prerequisites for the fix come first; the fix is the capstone but must land within Phase 2.*

1. **Migration**: add `HistoricalPrice`, `InstrumentListing`, `DataRefreshLog`, `InstrumentProfile.Type` + `.SubjectToVorabpauschale`; drop `YearEndPrice`.
2. **Price accessors**: `DbHistoricalPriceLookup` (+ `EarliestOnOrAfter`), `DbInstrumentMarketDataMap`, `DerivedInstrumentPriceProvider` + `IInstrumentPriceProvider`; nullable `IBasisInterestRateProvider`; extend seeder for the new tables/columns; remove the 30% TFS default.
3. **Stage A wiring**: calculator sources **year-end** price from the derived provider, formula unchanged; fixtures so `GermanTaxRegressionTests` passes **unchanged**. ✅ checkpoint.
4. **Yahoo provider** + incremental refresh service.
5. **ECB provider** + refresh service.
6. **BMF Basiszins source** + refresh service + manual edit.
7. **Stage B — Vorabpauschale correction** (§6.2): uniform year-start, distributions-in-cap, 1/12-on-final, fund-gating, Basiszins fail-fast; recompute baseline (§8); new tests (§13). ✅ **the bug is fixed here.**
8. **Instrument reference admin** (CRUD + upload, incl. classification).
9. **Clear services** (ledger + per-dataset).
10. **Data Administration UI** page + nav.
11. **DI wiring** + configuration options.
12. **Retire** Python scripts and `historical_prices.csv` / `market_data_mappings.json` as live inputs (keep seed CSV/JSON as bootstrap).
13. **Update `CLAUDE.md`**: tax guardrails describe uniform year-start rebasing, distributions-in-cap, 1/12-on-final, fund-gating, fail-fast Basiszins/classification; remove the resolved items from "known thin spots" and the 30%-default note.

---

## 15. Open risks
- **Acquisition-year interpretation (option b)**: we follow the literal statute; a future BMF-Schreiben could mandate purchase-price. Isolated to one branch of §6.2; documented in §6.5.
- **Regression-baseline arithmetic** is the highest-risk task: recomputed by hand + independently witnessed (§8).
- **Year-start data gaps**: a held fund without a price near 1 Jan blocks replay by design; Yahoo refresh pulls ≥5 years; blocking errors name the exact missing `(ISIN, currency, date)`.
- **Yahoo endpoint stability**: unofficial; mitigated by the interface boundary and committed parse fixtures.
- **BMF page format drift**: mitigated by defensive parsing + manual override.
- **ETC tax treatment** (`IE00B4ND3602`): kept subject-to-Vorabpauschale pending the user's determination; flippable in the instrument admin.
