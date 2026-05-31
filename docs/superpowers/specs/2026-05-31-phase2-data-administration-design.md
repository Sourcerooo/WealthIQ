# WealthIQ Phase 2 — Data Administration (Design)

- **Date:** 2026-05-31
- **Status:** Accepted (Design), ready for implementation plan
- **Branch:** `feature/Phase2`
- **Context:** Phase 2 of the WealthIQ v1 rebuild. Phase 1 (foundation/persistence), the import→persist pipeline, and tax-replay + dashboard are all implemented. This phase makes all data that the tax engine depends on **administrable from inside the app**: clearable, reloadable, and — for externally-sourced datasets — fetchable directly from the internet, replacing the two standalone Python scripts. A new "Data Administration" UI exposes every operation.

---

## 1. Goals & Non-Goals

**Goals**
1. Clear the imported ledger for a clean reload from scratch.
2. Clear and reload every seeded reference dataset (Basiszins, FX rates, historical prices, instruments).
3. Replace `scripts/download_price_history.py` with a native C# Yahoo Finance integration that stores OHLCV bars in SQLite, refreshes incrementally, and is currency/exchange-aware.
4. Replace `scripts/download_fx_rates.py` with a native C# ECB integration storing rates in SQLite.
5. Fetch the official Basiszins for the Vorabpauschale from an internet source, with a manual override.
6. View / edit / delete / upload instrument reference data.
7. Derive year-end prices from stored historical prices (drop the dedicated `YearEndPrice` table).
8. A single "Data Administration" UI page exposing all of the above.
9. All external data sources sit behind interfaces so providers can be swapped later.

**Non-Goals (unchanged from v1)**
- Portfolio valuation / charts, PDF export, additional brokers, strategies/backtesting, multi-base-currency.
- The "Vorabpauschale for a position held beyond the last ledger entry" as-of/through-year parameter remains a known thin spot and is **out of scope** here.

---

## 2. Locked decisions (this phase)

| Topic | Decision | Rationale |
|---|---|---|
| Yahoo acquisition | **Thin `HttpClient`**, no third-party NuGet; port the proven Python v8 chart call | All libraries wrap the same unofficial endpoint; several have an EU cookie/consent bug; the Python call already works from the EU; full control, hidden behind an interface |
| Yahoo politeness | One symbol at a time, fixed inter-request delay, exponential back-off + bounded retry on 429/5xx | Avoid throttling/blocking |
| Yahoo caching | Incremental: fetch only `(maxStoredDate+1 … today)` per symbol; immutable older bars never re-requested; explicit "Force full reload" wipes+refetches one symbol | Downloaded bars rarely change; minimize requests |
| Basiszins source | **Scrape the official BMF published value** behind `IBasisInterestRateSource`; manual override always available | One authoritative number per year; matches Finanzamt expectations; low risk |
| Seed files | **Keep committed CSV/JSON as offline bootstrap seed** (first run + CI/regression fixtures). Internet refresh writes to the DB only, never back to files | Keeps CI deterministic; clean separation of seed vs. live data |
| Multi-listing | **Design for safety**: support multiple listings per ISIN keyed by `(Isin, Currency)`; never mix currencies | User may hold the same ISIN in multiple currencies/exchanges |
| Year-end prices | **Derived** from `HistoricalPrice`; FX-converted in the calculator at the bar's own date; `YearEndPrice` table removed | Single source of truth; honors the "convert only at replay" FX rule |
| Year-end refactor safety | **Behavior-preserving**: engineer committed test fixtures so derived EUR year-end prices reproduce the current `prices.csv` values exactly, keeping `GermanTaxRegressionTests` green **unchanged** | The tax engine is the crown jewel; the refactor must be provably non-altering |

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
- Year-end prices become derived (see §6). The migration drops the table; `DbYearEndPriceProvider` is replaced by a derived provider.

### New small table — `DataRefreshLog` `(Dataset, LastRefreshedUtc, Note)`
- Powers the admin page's "last refreshed" status. One row per dataset, upserted on each refresh.

A single EF Core migration adds `HistoricalPrice`, `InstrumentListing`, `DataRefreshLog`, the `InstrumentProfile.Type` column, and drops `YearEndPrice`.

---

## 5. External providers

### 5.1 Yahoo historical prices — `YahooHistoricalPriceProvider : IHistoricalPriceProvider`
- Ports `download_price_history.py`: `GET https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?period1=…&period2=…&interval=1d&events=history` with a browser `User-Agent`, parses the `chart.result[0]` payload (timestamps + `indicators.quote[0]` OHLCV + `adjclose` + `meta.currency`), skips incomplete rows.
- **Interface:** `Task<HistoricalPriceFetchResult> FetchAsync(string providerSymbol, DateOnly from, DateOnly to, CancellationToken)` returning bars + the reported currency.
- **Politeness:** sequential per symbol; configurable inter-request delay; exponential back-off + bounded retries on 429/5xx; clear diagnostic on exhaustion.
- Registered via `IHttpClientFactory`.

### 5.2 ECB FX rates — `EcbFxRateProvider : IFxRateProvider`
- Ports `download_fx_rates.py`: fetches `https://www.ecb.europa.eu/stats/eurofxref/eurofxref-hist.xml`, parses daily cubes, emits `EUR=1.0` plus `currency_to_eur = 1/rate` for the supported currencies (USD/GBP/CHF, configurable), within a date window.
- **Interface:** `Task<IReadOnlyList<FxRateRecord>> FetchAsync(DateOnly from, DateOnly to, CancellationToken)`.

### 5.3 Basiszins — `BmfBasisInterestRateSource : IBasisInterestRateSource`
- Fetches the official BMF "Basiszins zur Berechnung der Vorabpauschale" published value for a given year (the single authoritative percentage, e.g. 2.53% for 2025, 3.20% for 2026) and returns `(year, rate)`.
- **Interface:** `Task<BasisInterestRateRecord?> FetchAsync(int year, CancellationToken)`.
- Parsing is defensive (the published figure is a single number); failure yields a diagnostic, never a silent wrong rate. Manual override in the UI is the fallback.

---

## 6. Year-end price derivation (behavior-preserving refactor)

**This is the highest-risk change; the tax engine's output must not change.**

Today `GermanTaxCalculator.PerformYearEndClosing` calls `IYearEndPriceProvider.GetPrice(isin, year)` → one **EUR** value, compared to the lot's EUR acquisition cost to compute appreciation for the Vorabpauschale cap.

New derivation, currency-aware:
1. Each `OpenLot` already carries its currency via `OpenUnitPrice.Currency`.
2. Resolve `(ISIN, lot currency)` → `ProviderSymbol` via `IInstrumentMarketDataMap`.
3. Fetch the **last trading bar of the year** for that symbol from `IHistoricalPriceLookup` (`LatestOnOrBefore` 31 Dec of `year`).
4. Convert that native-currency close → EUR via the **existing `FxConverter`** at the bar's own date (honors "convert only at replay, at the event's own time"). A missing FX rate or year-end bar is a **blocking error** — no silent fallback (unchanged contract: the calculator already throws when a year-end price is missing).

**Interface change (contained):**
`IYearEndPriceProvider.GetPrice(isin, year)` → `GetYearEndClose(isin, currency, year)` returning a small `YearEndQuote(decimal Close, Currency Currency, DateOnly AsOf)`. The **calculator** performs the EUR conversion, keeping all FX in one place. Only `PerformYearEndClosing` changes; the Vorabpauschale formula, lot handling, distribution offset, and Teilfreistellung logic are untouched.

**Non-regression strategy (mandatory):**
- Add committed historical-price fixtures under `data/test/configuration/` covering each regression instrument's year-ends, with native-currency closes + matching FX fixtures chosen so the **derived EUR year-end equals the current `prices.csv` value to the cent**.
- `GermanTaxRegressionTests` must then pass **with its existing expected values unchanged**, proving the refactor preserves behavior.
- Only if a value genuinely cannot be reproduced (e.g. a listing-currency mismatch in the current data) do we adjust the baseline — and then only with an explicit comment explaining the cause, per CLAUDE.md.
- Add focused unit tests for the derived provider: correct symbol resolution per currency, last-trading-day selection, FX conversion at the bar date, and blocking errors on missing bar/rate/listing.

---

## 7. Instrument reference administration

The editable "Instrument" is the union of two normalized tables, presented as one entity in the UI:
- **Profile:** `Isin`, `Name`, `Type`, `Teilfreistellungsquote`
- **Listings (0..n):** `Currency`, `ProviderSymbol`, `Provider`, `Exchange`, `Notes`

`IInstrumentReferenceAdmin` (Application service) provides:
- **List** all instruments with their listings.
- **Add / Edit** profile + listings; validation: ISIN format, `Teilfreistellungsquote ∈ [0,1]`, non-empty `ProviderSymbol` per listing, unique `(Isin, Currency)`.
- **Delete**: guarded — if the ISIN is referenced by ledger entries, warn before allowing deletion (don't silently break replay).
- **Upload**: accept the existing `instruments.json` (profiles) and `market_data_mappings.json` (listings) shapes so current files import cleanly; choice of **merge** or **replace** on upload.

---

## 8. Clearing & reload semantics

- **Clear ledger**: transactional delete of `PortfolioEntries` + `ImportBatches` + `ImportDiagnostics` + `Accounts`, with an option to also purge raw audit files in `data/app/audit`. Reference/market data untouched. Double-confirm.
- **Per-dataset Clear**: transactional truncate of the dataset's table(s).
- **Repopulate** offers two paths per applicable dataset:
  - **Re-seed from committed files** (offline bootstrap — reuses the existing seeder logic).
  - **Refresh from internet** (ECB / Yahoo / BMF) — writes to the DB only.
- Refresh services are idempotent: re-running merges/updates without duplicating (FX keyed by `(Date,Currency)`, prices by `(Symbol,Date)`, Basiszins by `Year`).

---

## 9. Data Administration UI (`/data-admin`)

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

## 10. DI / composition (`Web/Program.cs`)
- Register `IHttpClientFactory`; bind `YahooHistoricalPriceProvider`, `EcbFxRateProvider`, `BmfBasisInterestRateSource` to their interfaces.
- Repoint `IHistoricalPriceLookup` → `DbHistoricalPriceLookup`, `IInstrumentMarketDataMap` → `DbInstrumentMarketDataMap`, `IYearEndPriceProvider` → derived provider.
- Register the per-dataset refresh services, clear services, and `IInstrumentReferenceAdmin`.
- Configuration knobs (delays, retry counts, supported FX currencies, source URLs) via `appsettings` bound options, with sane defaults.

---

## 11. Testing

- **Regression (must stay green, unchanged):** `GermanTaxRegressionTests` with engineered historical-price + FX fixtures (see §6).
- **Derived year-end provider:** symbol resolution per currency, last-trading-day selection, FX at bar date, blocking errors.
- **Refresh services:** incremental gap fetch, dedup/idempotency, diagnostic on currency mismatch, back-off path (provider mocked — **no live network in tests**).
- **Providers:** parse fixtures captured from real Yahoo/ECB/BMF responses (committed sample payloads), asserting correct mapping; no live calls.
- **Instrument admin:** validation rules, delete-guard when referenced, upload merge/replace.
- **Clearing:** ledger clear leaves reference data intact and vice-versa; transactions roll back on failure.

---

## 12. Work units (for the implementation plan)
1. Migration: add `HistoricalPrice`, `InstrumentListing`, `DataRefreshLog`, `InstrumentProfile.Type`; drop `YearEndPrice`.
2. DB stores/lookups: `DbHistoricalPriceLookup`, `DbInstrumentMarketDataMap`; extend seeder for new tables.
3. Year-end derivation refactor + non-regression fixtures (§6).
4. Yahoo provider + incremental refresh service.
5. ECB provider + refresh service.
6. BMF Basiszins source + refresh service + manual edit.
7. Instrument reference admin (CRUD + upload).
8. Clear services (ledger + per-dataset).
9. Data Administration UI page + nav.
10. DI wiring + configuration options.
11. Retire Python scripts and `historical_prices.csv` / `market_data_mappings.json` as live inputs (keep seed CSV/JSON as bootstrap; remove Python from the repo or mark deprecated).

---

## 13. Open risks
- **Yahoo endpoint stability**: unofficial; mitigated by the interface boundary and committed parse fixtures.
- **BMF page format drift**: mitigated by defensive parsing + manual override.
- **Currency-mismatch in existing reference data**: the S&P 500 ETF (`IE00B3XXRP09`) is mapped to `VUSA.L` (GBP) yet its current `prices.csv` year-end is stored in EUR. §6's fixture engineering must reconcile this explicitly; surfacing such mismatches is a feature, not a bug.
