# WealthIQ Phase 2 — Data Administration & Vorabpauschale Correction (Design)

- **Date:** 2026-05-31
- **Status:** Accepted (Design), ready for implementation plan
- **Branch:** `feature/Phase2`
- **Context:** Phase 2 of the WealthIQ v1 rebuild. Phase 1 (foundation/persistence), the import→persist pipeline, and tax-replay + dashboard are all implemented. This phase (a) makes all data the tax engine depends on **administrable from inside the app** — clearable, reloadable, and fetchable directly from the internet, replacing the two standalone Python scripts — and (b) **fixes a confirmed correctness bug in the Vorabpauschale calculation for positions held across multiple calendar years**, which the new data layer makes possible. A new "Data Administration" UI exposes every operation.

---

## 1. Goals & Non-Goals

**Goals (priority order)**
1. **[HIGHEST PRIORITY] Fix the multi-year Vorabpauschale bug** (§6). For any year *after* a lot's acquisition year, the calculation must re-base to that year's **1 January value** (Rücknahmepreis zu Beginn des Kalenderjahres) per §18 InvStG — both for the Basisertrag base and the within-year appreciation cap — instead of the lot's original acquisition cost. By the end of Phase 2 this bug is correctly and verifiably fixed.
2. Store full daily historical prices in SQLite (the data prerequisite for Goal 1), currency/exchange-aware, replacing `scripts/download_price_history.py` with a native C# Yahoo Finance integration that refreshes incrementally.
3. Derive **year-end *and* year-start** prices from stored historical prices (drop the dedicated `YearEndPrice` table).
4. Replace `scripts/download_fx_rates.py` with a native C# ECB integration storing rates in SQLite.
5. Fetch the official Basiszins for the Vorabpauschale from an internet source (BMF), with a manual override.
6. Clear the imported ledger for a clean reload from scratch.
7. Clear and reload every seeded reference dataset (Basiszins, FX rates, historical prices, instruments).
8. View / edit / delete / upload instrument reference data.
9. A single "Data Administration" UI page exposing all of the above.
10. All external data sources sit behind interfaces so providers can be swapped later.

**Non-Goals (unchanged from v1)**
- Portfolio valuation / charts, PDF export, additional brokers, strategies/backtesting, multi-base-currency.
- The "Vorabpauschale for a position held beyond the last ledger entry" as-of/through-year parameter remains a known thin spot and is **out of scope** here (the calculator still replays only up to the last ledger entry year).

---

## 2. Locked decisions (this phase)

| Topic | Decision | Rationale |
|---|---|---|
| **Multi-year Vorabpauschale** | **Fix within Phase 2 (highest priority).** Re-base to the year-start value for non-acquisition years per §18 InvStG | Confirmed correctness bug affecting the *normal* buy-and-hold case; the project's purpose is a Finanzamt-grade report |
| Implementation sequencing | **Two stages**: (1) build the data layer + price accessors and prove the year-end path is behavior-preserving; (2) add year-start derivation + corrected formula as the capstone | Isolates "did the plumbing change a number?" from "did the formula deliberately change a number?" — de-risks touching the engine |
| Yahoo acquisition | **Thin `HttpClient`**, no third-party NuGet; port the proven Python v8 chart call | All libraries wrap the same unofficial endpoint; several have an EU cookie/consent bug; the Python call already works from the EU; full control, hidden behind an interface |
| Yahoo politeness | One symbol at a time, fixed inter-request delay, exponential back-off + bounded retry on 429/5xx | Avoid throttling/blocking |
| Yahoo caching | Incremental: fetch only `(maxStoredDate+1 … today)` per symbol; immutable older bars never re-requested; explicit "Force full reload" wipes+refetches one symbol | Downloaded bars rarely change; minimize requests |
| Basiszins source | **Scrape the official BMF published value** behind `IBasisInterestRateSource`; manual override always available | One authoritative number per year; matches Finanzamt expectations; low risk |
| Seed files | **Keep committed CSV/JSON as offline bootstrap seed** (first run + CI/regression fixtures). Internet refresh writes to the DB only, never back to files | Keeps CI deterministic; clean separation of seed vs. live data |
| Multi-listing | **Design for safety**: support multiple listings per ISIN keyed by `(Isin, Currency)`; never mix currencies | User may hold the same ISIN in multiple currencies/exchanges |
| Price source for tax | **Derived** from `HistoricalPrice`; each Rücknahmepreis FX-converted at its **own bar date**; `YearEndPrice` table removed | Single source of truth; honors the "convert only at replay, at the event's own time" FX rule |

---

## 3. Architecture: Provider → Store → Refresh

For each externally-sourced dataset, three concerns are separated so providers are swappable and the DB remains the single source of truth. The Domain and tax engine never touch the internet.

| Concern | Lives in | Talks to | New/changed types |
|---|---|---|---|
| **Source/Provider** (fetch) | `Application` interface, `Infrastructure` adapter | the internet | `IHistoricalPriceProvider` → `YahooHistoricalPriceProvider`; `IFxRateProvider` → `EcbFxRateProvider`; `IBasisInterestRateSource` → `BmfBasisInterestRateSource` |
| **Lookup/Store** (read DB) | `Application` interface, `Infrastructure` adapter | SQLite | `IHistoricalPriceLookup` → **`DbHistoricalPriceLookup`** (new, replaces `CsvHistoricalPriceLookup`); `IFxRateLookup` → `DbFxRateLookup` (exists); `IInstrumentMarketDataMap` → **`DbInstrumentMarketDataMap`** (new, replaces `JsonInstrumentMarketDataMap`); `IBasisInterestRateProvider` → `DbBasisInterestRateProvider` (exists) |
| **Refresh service** (orchestrate) | `Application` | provider + store | one service per dataset; fetches, dedups/caches, upserts, returns a structured result (`Added/Updated/Skipped` + `ImportDiagnostic` list) |

Dependency direction is preserved: providers/stores live in `Infrastructure`, interfaces in `Application`, and only `Web` wires them.

### Fail-fast & diagnostics
Refresh operations follow the existing import philosophy: collect structured diagnostics (`Info/Warning/Error/Fatal`), and abort the dataset's transaction if any blocking diagnostic occurs — no silent drops. A fetched bar whose reported currency ≠ the configured listing currency is a **blocking error**.

---

## 4. Data model changes

### New table — `HistoricalPrice` (replaces `historical_prices.csv`)
- **Key:** `(ProviderSymbol, Date)`
- **Columns:** `ProviderSymbol` (string), `Date` (DateOnly), `Currency` (string), `Open`, `High`, `Low`, `Close`, `AdjustedClose` (decimal), `Volume` (long)
- Currency is intrinsic to the listing; distinct symbols (`VUSA.L` GBP vs `CSPX.AS` EUR) keep currencies from mixing.

### New table — `InstrumentListing` (replaces `market_data_mappings.json`, multi-currency-safe)
- **Key:** `(Isin, Currency)` — enables the same ISIN in EUR *and* GBP without mixing
- **Columns:** `Isin`, `Currency`, `Provider`, `ProviderSymbol`, `Exchange` (nullable), `Notes` (nullable)
- A lot resolves `(ISIN, lot currency)` → `ProviderSymbol`. A missing listing for a held `(ISIN, currency)` is a blocking error at tax replay.

### Keep — `InstrumentProfile`
- `Isin`, `Name`, `Teilfreistellungsquote`; **add** `Type` (string, mirrors `instruments.json`'s `type`). Editable in UI.

### Keep — `FxRate` `(Date, Currency) → RateToEur`
- Now also written by the ECB refresh, in addition to file seeding.

### Keep — `BasisInterestRate` `(Year) → Rate`
- Now also written by the BMF refresh and manual edits.

### Remove — `YearEndPrice` table
- Year-end **and** year-start prices become derived from `HistoricalPrice` (see §5.4 and §6). The migration drops the table; `DbYearEndPriceProvider` is replaced by a derived provider.

### New small table — `DataRefreshLog` `(Dataset, LastRefreshedUtc, Note)`
- Powers the admin page's "last refreshed" status. One row per dataset, upserted on each refresh.

A single EF Core migration adds `HistoricalPrice`, `InstrumentListing`, `DataRefreshLog`, the `InstrumentProfile.Type` column, and drops `YearEndPrice`.

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
- **Coverage note:** the corrected calculation needs an FX rate at *every* year-start and year-end bar date for non-EUR lots. The ECB series excludes weekends/holidays, and the FX lookup already supports roll-forward (`NextAvailableOnOrAfter`); the calculator converts at the **bar's own date**, which is a trading day and therefore an ECB business day, so a rate is normally present. A genuinely missing rate is a blocking error (no silent fallback).

### 5.3 Basiszins — `BmfBasisInterestRateSource : IBasisInterestRateSource`
- Fetches the official BMF "Basiszins zur Berechnung der Vorabpauschale" published value for a given year (the single authoritative percentage, e.g. 2.53% for 2025, 3.20% for 2026) and returns `(year, rate)`.
- **Interface:** `Task<BasisInterestRateRecord?> FetchAsync(int year, CancellationToken)`.
- Parsing is defensive (the published figure is a single number); failure yields a diagnostic, never a silent wrong rate. Manual override in the UI is the fallback.

### 5.4 Price accessors (the data prerequisite for §6)
- **`DbHistoricalPriceLookup : IHistoricalPriceLookup`** — reads `HistoricalPrice` by `ProviderSymbol`. Extend `PriceLookupDateHandling` with **`EarliestOnOrAfter`** (for year-start lookups) alongside the existing `ExactDate` and `LatestOnOrBefore`.
- **`DbInstrumentMarketDataMap : IInstrumentMarketDataMap`** — resolves `(ISIN, Currency)` → `ProviderSymbol` from `InstrumentListing`. Missing mapping is a blocking error.
- **New tax-facing accessor — `IInstrumentPriceProvider`** (Application, `Tax` namespace), implemented by **`DerivedInstrumentPriceProvider`** (Infrastructure):
  ```csharp
  public readonly record struct InstrumentQuote(decimal Close, Currency Currency, DateOnly AsOf);

  public interface IInstrumentPriceProvider
  {
      // Returns the relevant Rücknahmepreis (close) for the instrument's listing in `currency`,
      // resolved by date handling. Close is in `Currency`; the CALLER converts to EUR.
      // Null only when no listing/bar exists; the calculator turns null into a blocking error.
      InstrumentQuote? GetQuote(string isin, Currency currency, DateOnly pricingDate, PriceQuoteHandling handling);
  }

  public enum PriceQuoteHandling { LatestOnOrBefore, EarliestOnOrAfter, ExactDate }
  ```
  `DerivedInstrumentPriceProvider` resolves the symbol via `IInstrumentMarketDataMap`, reads the bar via `IHistoricalPriceLookup`, and returns `(close, barCurrency, barDate)`. It asserts `barCurrency == currency` (else blocking error). **It does not do FX** — conversion stays in the calculator, in one place, per the architecture rule.
- This **replaces** `IYearEndPriceProvider` / `DbYearEndPriceProvider` entirely.

---

## 6. The Vorabpauschale correction (core deliverable)

### 6.1 Current (buggy) behavior — for reference
`GermanTaxCalculator.PerformYearEndClosing` uses, for **every** year a lot is held:
- base = the lot's **original acquisition cost** per share in EUR (FX at trade date, fees included),
- `Basisertrag = base × Basiszins(Y) × 0.7 × months/12`,
- `appreciation = max(0, yearEndPrice − base)` (cumulative since purchase),
- `Vorab = min(Basisertrag, appreciation) − distributions`.

The `months/12` factor is pro-rated only in the acquisition year (`12 − OpenTradeDate.Month + 1`) and is `12/12` afterwards. The acquisition year is therefore already correct; **only non-acquisition years are wrong** — they should re-base to the year-start value.

### 6.2 Corrected algorithm (§18 InvStG)

For each open **long** lot with `RemainingQuantity > 0` held at the end of calendar year **Y**:

```
lotCurrency = lot.OpenUnitPrice.Currency
basisFactor = Basiszins(Y) × 0.7

# --- per-share START value in EUR + month factor ---
if lot.OpenTradeDate.Year == Y:                      # ACQUISITION YEAR — unchanged from today
    startValueEur = CalculateRemainingAcquisitionPriceInEur(lot)   # cost incl. fees, FX @ trade date
    monthsFactor  = (12 - lot.OpenTradeDate.Month + 1) / 12
else:                                                 # HELD FROM A PRIOR YEAR — the fix
    q = priceProvider.GetQuote(isin, lotCurrency, Jan 1 of Y, EarliestOnOrAfter)   # first trading bar of Y
    if q is null: BLOCKING ERROR
    startValueEur = fxConverter.Convert(Money(q.Close, q.Currency), q.AsOf).Amount   # FX @ year-start bar date
    monthsFactor  = 1

# --- per-share END value in EUR ---
e = priceProvider.GetQuote(isin, lotCurrency, Dec 31 of Y, LatestOnOrBefore)        # last trading bar of Y
if e is null: BLOCKING ERROR
endValueEur = fxConverter.Convert(Money(e.Close, e.Currency), e.AsOf).Amount         # FX @ year-end bar date

# --- Vorabpauschale (per share) ---
basisErtrag    = startValueEur × basisFactor × monthsFactor
wertsteigerung = max(0, endValueEur − startValueEur)        # appreciation WITHIN year Y
grossVorab     = min(basisErtrag, wertsteigerung)
distPerShare   = Σ distributions for this lot in Y, paid on/after lot.OpenTradeDate   # unchanged
netVorab       = max(0, grossVorab − distPerShare)
if netVorab <= 0: skip lot

totalVorab     = netVorab × lot.RemainingQuantity
# accumulate on the lot (AccumulatedVorabpauschale) and post the ledger entry,
# Teilfreistellung applied, posted to (Y+1, 1 Jan) — ALL unchanged from today.
```

**What changes vs. today:** only the `else` branch (non-acquisition years) — `startValueEur` becomes the **year-start market price** instead of the original cost, and `monthsFactor` is `1`. Everything else — acquisition-year handling, distribution offset, Teilfreistellung, accumulation, posting to year+1, deduction of previously-taxed Vorabpauschale at sale — is **untouched**.

### 6.3 Explicit, documented assumptions (so they are conscious choices, not accidents)

1. **Year-start = first trading day of the year.** "Rücknahmepreis zu Beginn des Kalenderjahres" is taken as the first historical bar with `Date ≥ 1 Jan` (`EarliestOnOrAfter`), symmetric with year-end (last bar `≤ 31 Dec`). The tiny New-Year gap between one year's last bar and the next year's first bar is taxed in neither year — accepted. (Alternative considered: prior-year 31 Dec close; rejected for being less literal and asymmetric.)
2. **Per-component FX.** Each Rücknahmepreis is converted to EUR at **its own bar date** (year-start at the year-start bar date, year-end at the year-end bar date, acquisition cost at trade date). This matches the project's core FX rule and the existing realized-gain methodology. (Alternative considered: compute Vorabpauschale in fund currency, convert the *result* at the Y+1 inflow date; rejected for inconsistency with the rest of the engine. Documented so it can be revisited if a Finanzamt requires otherwise.)
3. **Acquisition-year base keeps fees** (existing `CalculateRemainingAcquisitionPriceInEur`); year-start base is a pure market close (no fees). This mirrors current behavior for the acquisition year and the legal use of the market Rücknahmepreis thereafter. Not changed in this phase.
4. **Quotes are per share** (NAV per unit), consistent with `OpenUnitPrice` being per share.

### 6.4 Edge cases (all fail-fast, no silent fallback)
- No `InstrumentListing` for `(ISIN, lotCurrency)` → blocking error naming the ISIN + currency.
- No year-start bar on/after 1 Jan of Y (e.g. fund not yet listed) for a *non-acquisition* lot → blocking error.
- No year-end bar on/before 31 Dec of Y → blocking error.
- Missing FX rate at a bar date → blocking error (existing `FxConverter` behavior).
- `bar.Currency != lotCurrency` → blocking error (guards against a mis-mapped listing).
- `endValueEur ≤ startValueEur` → `wertsteigerung = 0` → no Vorabpauschale (correct).
- Partial-sold lot: value `RemainingQuantity`; `OpenTradeDate.Year` still determines acquisition-vs-prior-year.

---

## 7. Staged delivery & regression strategy (de-risking the engine change)

**Stage A — data layer + accessors, behavior-preserving checkpoint.**
- Build `HistoricalPrice`, `InstrumentListing`, `DbHistoricalPriceLookup` (+ `EarliestOnOrAfter`), `DbInstrumentMarketDataMap`, `DerivedInstrumentPriceProvider`, and the Yahoo/ECB/BMF providers + refresh services.
- Wire the calculator to source the **year-end** price from `DerivedInstrumentPriceProvider` **while keeping the current (acquisition-cost) formula**.
- Engineer committed historical-price + FX fixtures under `data/test/configuration/` so the **derived EUR year-end equals the current `prices.csv` value to the cent**.
- **`GermanTaxRegressionTests` must pass with its existing expected values UNCHANGED.** This proves the new data path did not move any number — isolating plumbing risk from formula risk.

**Stage B — corrected formula, deliberate behavior change (the capstone).**
- Implement §6.2 (year-start re-basing for non-acquisition years).
- **Recompute the regression baseline** using the methodology in §8 and update `GermanTaxRegressionTests` with the new expected values, each accompanied by a comment showing the arithmetic and *why* it changed (per CLAUDE.md "update expected values deliberately").
- Add the new targeted tests in §9.
- At this point the bug is fixed and the suite is green on the corrected numbers. **Phase 2 is not complete until Stage B is done.**

---

## 8. Regression-baseline recomputation methodology (do this exactly)

The current `GermanTaxRegressionTests` asserts exact 2024 disposal + Vorabpauschale figures from `data/test`. The test instruments are held multiple years, so Stage B changes their Vorabpauschale. To regenerate the baseline without mistakes:

1. **Enumerate Vorabpauschale-bearing lots** from the test statements: for each, record `ISIN`, currency, `OpenTradeDate`, quantity (and any partial sales), per year up to 2024.
2. **Assemble fixtures** so that, for every `(ISIN, currency, year)` a held lot needs, the committed `HistoricalPrice` fixture contains:
   - the **first** trading bar of the year (year-start), and
   - the **last** trading bar of the year (year-end),
   in the lot's currency, plus the **FX fixtures** for those two dates.
   Choose fixture closes deliberately (round, documented values) — these are the single source of truth for the expected numbers.
3. **Compute per lot, per year** with §6.2:
   - acquisition year → `startValueEur` = acquisition cost (as the existing test already implies), `monthsFactor` pro-rated;
   - later years → `startValueEur` = year-start fixture close → EUR @ year-start date, `monthsFactor = 1`;
   - `endValueEur` = year-end fixture close → EUR @ year-end date;
   - `basisErtrag`, `wertsteigerung`, `min`, minus distributions, `× remaining qty`, `× (1 − tfs)` for the taxable amount.
4. **Sum** to the report figures the test asserts; record the arithmetic in a worked table inside the implementation plan **and** as comments in the test.
5. **Cross-check**: a standalone, committed unit test (`Vorabpauschale_MultiYearHold_RebasesToYearStart`) reproduces one lot's multi-year numbers from first principles so the regression baseline has an independent witness.
6. **Sanity check** against §6.1: every changed Vorabpauschale should move in the expected direction (typically *up* for an appreciating multi-year hold, because the year-start base exceeds the original cost).

> Note on the `IE00B3XXRP09` listing/currency mismatch (mapped to `VUSA.L` GBP, but `prices.csv` stored EUR year-end values): Stage A's fixtures must reconcile this explicitly — either model the lot in GBP with GBP fixtures + FX, or add a EUR listing if the lot was actually traded in EUR. The correct currency comes from the lot's `OpenUnitPrice.Currency` in the test statements; the fixtures follow that. Surfacing this is a feature.

---

## 9. Instrument reference administration

The editable "Instrument" is the union of two normalized tables, presented as one entity in the UI:
- **Profile:** `Isin`, `Name`, `Type`, `Teilfreistellungsquote`
- **Listings (0..n):** `Currency`, `ProviderSymbol`, `Provider`, `Exchange`, `Notes`

`IInstrumentReferenceAdmin` (Application service) provides:
- **List** all instruments with their listings.
- **Add / Edit** profile + listings; validation: ISIN format, `Teilfreistellungsquote ∈ [0,1]`, non-empty `ProviderSymbol` per listing, unique `(Isin, Currency)`.
- **Delete**: guarded — if the ISIN is referenced by ledger entries, warn before allowing deletion (don't silently break replay).
- **Upload**: accept the existing `instruments.json` (profiles) and `market_data_mappings.json` (listings) shapes so current files import cleanly; choice of **merge** or **replace** on upload.

---

## 10. Clearing & reload semantics

- **Clear ledger**: transactional delete of `PortfolioEntries` + `ImportBatches` + `ImportDiagnostics` + `Accounts`, with an option to also purge raw audit files in `data/app/audit`. Reference/market data untouched. Double-confirm.
- **Per-dataset Clear**: transactional truncate of the dataset's table(s).
- **Repopulate** offers two paths per applicable dataset:
  - **Re-seed from committed files** (offline bootstrap — reuses the existing seeder logic).
  - **Refresh from internet** (ECB / Yahoo / BMF) — writes to the DB only.
- Refresh services are idempotent: re-running merges/updates without duplicating (FX keyed by `(Date,Currency)`, prices by `(Symbol,Date)`, Basiszins by `Year`).

---

## 11. Data Administration UI (`/data-admin`)

A single MudBlazor page, one collapsible card per dataset, each showing **status** and **actions**. Nav link added to `MainLayout`.

| Card | Status shown | Actions |
|---|---|---|
| **Ledger** | entries, accounts, batches | Clear ledger (± purge raw files) — double-confirm |
| **Historical prices** | symbols, per-symbol date range, last refreshed | Refresh (incremental), Force full reload (per symbol/all), Clear |
| **FX rates (ECB)** | currencies, date range, last refreshed | Refresh, Clear, Re-seed from file |
| **Basiszins (BMF)** | years present + rate, last refreshed | Refresh from BMF, Manual add/edit, Clear, Re-seed |
| **Instruments** | count | Table with inline edit/delete, Add, Upload (json, merge/replace) |

Long-running refreshes run asynchronously with progress and a result summary (added/updated/skipped + diagnostics), reusing the diagnostic-table style already on the Import page.

---

## 12. DI / composition (`Web/Program.cs`)
- Register `IHttpClientFactory`; bind `YahooHistoricalPriceProvider`, `EcbFxRateProvider`, `BmfBasisInterestRateSource` to their interfaces.
- Repoint `IHistoricalPriceLookup` → `DbHistoricalPriceLookup`, `IInstrumentMarketDataMap` → `DbInstrumentMarketDataMap`; register `IInstrumentPriceProvider` → `DerivedInstrumentPriceProvider`; **remove** `IYearEndPriceProvider`.
- Register the per-dataset refresh services, clear services, and `IInstrumentReferenceAdmin`.
- Configuration knobs (delays, retry counts, supported FX currencies, source URLs) via `appsettings` bound options, with sane defaults.

---

## 13. Testing

- **Stage A regression (must stay green, UNCHANGED):** `GermanTaxRegressionTests` with engineered historical-price + FX fixtures reproducing current `prices.csv` year-end EUR values (§7 Stage A).
- **Stage B regression (deliberately updated):** `GermanTaxRegressionTests` recomputed per §8, with arithmetic comments.
- **New corrected-calc tests:**
  - `Vorabpauschale_AcquisitionYear_UsesAcquisitionCostAndProRatesMonths` (unchanged behavior preserved).
  - `Vorabpauschale_MultiYearHold_RebasesToYearStart` (the fix; independent first-principles witness).
  - `Vorabpauschale_NonEurLot_ConvertsYearStartAndYearEndAtOwnDates`.
  - Blocking-error cases: missing listing, missing year-start bar, missing year-end bar, missing FX, currency mismatch.
- **Derived price provider:** symbol resolution per currency, `EarliestOnOrAfter` / `LatestOnOrBefore` selection, currency-mismatch guard.
- **Refresh services:** incremental gap fetch, dedup/idempotency, diagnostic on currency mismatch, back-off path (provider mocked — **no live network in tests**).
- **Providers:** parse fixtures captured from real Yahoo/ECB/BMF responses (committed sample payloads), asserting correct mapping; no live calls.
- **Instrument admin:** validation rules, delete-guard when referenced, upload merge/replace.
- **Clearing:** ledger clear leaves reference data intact and vice-versa; transactions roll back on failure.

---

## 14. Work units (for the implementation plan; order matters)

*Prerequisites for the fix come first; the fix is the capstone but must land within Phase 2.*

1. **Migration**: add `HistoricalPrice`, `InstrumentListing`, `DataRefreshLog`, `InstrumentProfile.Type`; drop `YearEndPrice`.
2. **Price accessors**: `DbHistoricalPriceLookup` (+ `EarliestOnOrAfter`), `DbInstrumentMarketDataMap`, `DerivedInstrumentPriceProvider` + `IInstrumentPriceProvider`; extend seeder for the new tables.
3. **Stage A wiring**: calculator sources **year-end** price from the derived provider, formula unchanged; engineer fixtures so `GermanTaxRegressionTests` passes **unchanged**. ✅ checkpoint.
4. **Yahoo provider** + incremental refresh service.
5. **ECB provider** + refresh service.
6. **BMF Basiszins source** + refresh service + manual edit.
7. **Stage B — Vorabpauschale correction** (§6.2): year-start re-basing; recompute baseline (§8); new tests (§13). ✅ **the bug is fixed here.**
8. **Instrument reference admin** (CRUD + upload).
9. **Clear services** (ledger + per-dataset).
10. **Data Administration UI** page + nav.
11. **DI wiring** + configuration options.
12. **Retire** Python scripts and `historical_prices.csv` / `market_data_mappings.json` as live inputs (keep seed CSV/JSON as bootstrap; remove/deprecate Python).
13. **Update `CLAUDE.md`**: tax guardrails now describe year-start re-basing; remove the multi-year item from "known thin spots".

---

## 15. Open risks
- **Regression-baseline arithmetic** is the highest-risk task: it must be recomputed by hand and independently witnessed (§8). Mitigation: the standalone first-principles test + worked table in the plan.
- **Year-start data gaps**: a fund without a price near 1 Jan of a held year blocks replay by design. Mitigation: Yahoo refresh pulls ≥5 years of daily history; blocking errors name the exact missing `(ISIN, currency, date)`.
- **Yahoo endpoint stability**: unofficial; mitigated by the interface boundary and committed parse fixtures.
- **BMF page format drift**: mitigated by defensive parsing + manual override.
- **FX-conversion interpretation** for Vorabpauschale (§6.3 assumption 2): documented and isolated in the calculator so it can be revisited without touching the data layer.
