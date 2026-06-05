# Phase 3 — Data Visualization & Insights — Design

**Date:** 2026-06-06
**Status:** Approved (brainstorming) → ready for implementation planning
**Scope owner:** single-user local WealthIQ tool

## Goal

Make the imported and reference data inspectable, and make the German tax report
verifiable down to the source. Four workstreams:

1. A small **pie-chart tooltip fix** on the Steuerreport.
2. A new **Data Browser** navigation area to visualize ledger, market, and FX data.
3. **Steuerreport verifiability** enhancements (detailed Verkäufe drill-down,
   Zinsen/Quellensteuer column fixes).
4. An **editable Basiszins table** added to the existing Marktdaten page.

Out of scope (unchanged from v1): portfolio valuation/charts on the report,
PDF export, additional brokers, multi-base-currency.

---

## Workstream 1 — Pie-chart tooltip fix

**Problem:** the Steuerreport composition donut feeds `(double)decimal` segment
values directly, so binary-float artifacts surface in the hover tooltip as long
decimal tails even though the values are euro amounts.

**Fix:** round each segment value to 2 decimals (`Math.Round(value, 2)`) where
`CompositionSeries` is constructed in `Steuerreport.razor`. Purely presentational;
no data-flow change. (`MudBlazor` 9.5.0 donut tooltips render the data value
directly; rounding the source data is the robust fix.)

**Acceptance:** hovering any donut segment shows a euro value with at most two
decimals.

---

## Workstream 2 — Data Browser

A new left-nav group **"Daten ansehen"**, placed between *Daten erfassen* and
*Stammdaten*, with three pages:

| # | Route | Page | Purpose |
|---|-------|------|---------|
| 1 | `/browse/ledger` | Ledger Data | Raw imported ledger, split by entry kind |
| 2 | `/browse/prices` | Kurschart | Per-symbol adjusted OHLC candlestick |
| 3 | `/browse/fx` | Wechselkurse | Per-currency FX line chart |

### Data-access pattern

Follow the existing `DataAdmin`/`Audit` precedent: pages inject
`WealthIqDbContext` directly for market/FX reads and `ILedgerStore.LoadLedgerAsync()`
for the ledger. This is consistent with the codebase (Web already references
Infrastructure as the composition root) and appropriate for a single-user local
tool. The only logic extracted into a reusable, unit-testable helper is the
**adjusted-OHLC derivation**.

### 2.1 Ledger Data (`/browse/ledger`)

- Load the ledger once via `ILedgerStore.LoadLedgerAsync()` and the instrument
  catalog (same shape as `Audit.razor` does today).
- Render separate `MudTable`s grouped by entry kind, in this order:
  - **Trades** (`TradeEntry`): date, side, symbol, ISIN, quantity, unit price,
    fees, taxes, currency.
  - **Dividenden** (`CashEntry` where `CashFlowType.Dividend`): date, symbol, ISIN,
    gross amount, fees, taxes, currency.
  - **Zinsen** (`CashEntry` where `CashFlowType.Interest`): date, gross amount,
    fees, taxes, currency. (No ISIN — interest has none.)
  - **Quellensteuer** (`CashEntry` where `CashFlowType.WithholdingTax`): date,
    related symbol/ISIN if present, gross amount, currency.
  - **Sonstige Buchungen**: any remaining `CashEntry` cash-flow types.
- Each row exposes an "Anzeigen" link to `/audit` (reuse existing ISIN filter
  where an ISIN is present) for provenance.
- Read-only. Amounts shown in their **original currency** (per the canonical-ledger
  rule — no EUR conversion here).

### 2.2 Candlestick chart (`/browse/prices`)

**Library:** TradingView **Lightweight Charts** (Apache-2.0, free for personal
use), loaded as an ES module from `wwwroot/`. No Blazor wrapper exists, so we
hand-write a thin JS-interop module plus a reusable Razor component.

**Reusable component** `Components/Shared/LightweightChart.razor`
(+ `wwwroot/lightweight-chart.js`):
- Parameters: chart type (candlestick | line), series data, dark/light flag.
- Responsibilities: lazy-load the library module, create the chart on first
  render, push/replace series data on parameter change, sync colors to the active
  theme, handle container resize, and dispose the chart (`IAsyncDisposable` +
  JS-side teardown) to avoid leaks on Blazor Server navigation.
- Theme colors pulled from the WealthIQ palette (emerald up / red down candles,
  navy background, muted grid) to match the design.

**Page:**
- Top bar: a searchable `MudAutocomplete<>` listing instruments from
  `InstrumentListings` (display "symbol — ISIN (currency)"; search over both).
- On selection: query `HistoricalPrices` for that `ProviderSymbol` (ordered by
  date), derive **adjusted OHLC**, and render daily candles. Native zoom/scroll.
- Empty state when the chosen listing has no bars yet (prompts the user to fetch
  them on Marktdaten).

**Adjusted-OHLC derivation** — extracted as a pure helper (in Application,
unit-tested): for each bar `factor = AdjustedClose / Close` (guard `Close > 0`);
adjusted bar = `(Open*factor, High*factor, Low*factor, AdjustedClose)`. Produces a
split/dividend-consistent candle series. `decimal` math, rounded to a sensible
price precision for display.

### 2.3 FX chart (`/browse/fx`)

- Same `LightweightChart` component in **line** mode.
- Top bar: dropdown of currencies present in `FxRates`, each labelled **"X / EUR"**.
- Series: `RateToEur` over `Date` for the chosen currency (one value/day → line).
- Native zoom/drag. Empty state when no rates exist for a currency.

**Acceptance (W2):** each page renders, the dropdowns search/select, the candle
chart shows adjusted daily bars with working zoom/scroll, the FX chart shows the
daily line, and all three theme correctly in dark and light mode.

---

## Workstream 3 — Steuerreport verifiability

### 3.1 Detailed Verkäufe drill-down

**Domain change:** extend `GermanTaxEntry` with two fields:
- `OpenedOn` (`DateOnly`) — the matched lot's acquisition date.
- `Fees` (`decimal`, EUR) — fees attributable to the sale.

Both are populated in `GermanTaxCalculator` from the `TradeRealizationEntry` /
lot consumption already available at the Sell-entry construction site
(`OpenedOn`, `Fees`). Other entry kinds leave them at default `0`/`default`.

**UI:** a new expansion panel **"Verkäufe — Details"** on the Steuerreport renders
the full per-sale table:
- Opened (`OpenedOn`), Closed (`Date`), Shares (`QuantitySold`),
  Buy price (`AcquisitionCosts / QuantitySold`),
  Sell price (`SaleProceeds / QuantitySold`),
  Fees, Raw P&L (`RawAmount`), Used Vorabpauschale, Taxable.
- Each detail row has an anchor id, and a **link to `/audit`** (ISIN-filtered) for
  the imported source entry.

**Linking:** the existing summary "Verkäufe" table's **"Anzeigen"** button (in the
*Quelle* column) now scrolls to the matching row in the detail panel (in-page
anchor / `scrollIntoView` via JS), rather than jumping straight to `/audit`.
The detail row is the one that then links onward to `/audit`.

**Regression test:** `GermanTaxRegressionTests` expected values updated
deliberately to include the new fields; the change is documented in the test and
commit message.

### 3.2 Zinsen — remove ISIN column

Interest has no ISIN. The shared `EntryTable` render fragment gains a flag (e.g.
`showIsin`) so the Zinsen table omits the ISIN column; other tables keep it.

### 3.3 Quellensteuer — show origin

Add an **origin** descriptor to the withholding `GermanTaxEntry` so the
Quellensteuer table shows where each withholding tax came from — the originating
instrument symbol, or a type label ("Zinsen") when it stems from an interest
cash-flow rather than a security. Implemented as a small `GermanTaxCalculator`
change at the `WithholdingTax` branch (it already resolves
`RelatedInstrumentId ?? CashInstrumentId`; the origin label is derived there) and
surfaced as a new "Herkunft" column in the withholding table.

**Acceptance (W3):** the Verkäufe detail table shows opened/closed/shares/buy/
sell/fees/PnL; "Anzeigen" scrolls to the detail row which links to `/audit`; the
Zinsen table has no ISIN column; the Quellensteuer table shows a Herkunft column;
the regression test passes with updated, documented expectations.

---

## Workstream 4 — Editable Basiszins table (Marktdaten page)

On the existing **Stammdaten → Marktdaten** page (`/data-admin`), the Basiszins
section currently shows only summary counts (min/max year, count). Add an
**editable table** listing every stored year → rate row:
- Inline edit of the rate (reuse `BasisInterestRateRefreshService.SetManualAsync`,
  which upserts).
- Delete a row (one small new store/service method —
  `IBasisInterestRateStore.Delete(year)` + a thin service call).
- **Creation of new years stays where it is** (manual year/rate entry + BMF
  refresh + reseed) — unchanged.

Rows read directly from `Db.BasisInterestRates` (consistent with the page's
existing direct-`Db` usage), refreshed after each edit/delete via the page's
existing `LoadStatusAsync`.

**Acceptance (W4):** the Marktdaten Basiszins section lists all stored rows;
editing a rate persists and re-renders; deleting a row removes it; adding new
years still works via the existing controls.

---

## Architecture & layering notes

- **No Domain outward dependency.** The only Domain/Application change is the
  `GermanTaxEntry` field additions (3.1/3.3) and the adjusted-OHLC helper (2.2),
  both pure.
- **Canonical-ledger rules intact.** Ledger Data shows original-currency amounts;
  no EUR conversion introduced in the browser. FX/price reads are presentation-only.
- **Web stays the composition root.** New pages live in `WealthIQ.Web`; JS assets
  in `wwwroot/`. Lightweight Charts is a static asset/module, not a NuGet package.
- **Reusable presentational pieces** follow the existing `Components/Shared/`
  convention (no `Class`/`Style` params; wrap for spacing; `aria-label` on
  icon-only buttons).

## Testing

- Unit-test the adjusted-OHLC helper (factor application, `Close = 0` guard,
  precision).
- Update `GermanTaxRegressionTests` for the new `GermanTaxEntry` fields with exact,
  documented expectations.
- Manual `dotnet run` smoke test for every new/changed Blazor page (per project
  memory: build + xUnit do not catch Blazor render errors), covering: each Data
  Browser page renders and selects data; charts theme in dark/light; the Verkäufe
  drill-down link scrolls and the onward `/audit` link works; the Basiszins table
  edits/deletes.
- No live network, no real-time, fixed reference data in automated tests.

## Risks / known thin spots

- Lightweight Charts is JS-interop only — disposal and theme-sync on Blazor Server
  navigation need care (handled in the `LightweightChart` component).
- Adjusted OHLC is derived from a single `AdjustedClose` (we don't store adjusted
  O/H/L); the factor approach is the standard reconstruction and is good enough for
  visual inspection, not statutory pricing (the tax engine continues to use raw
  `Close`, never adjusted — unchanged).
- Withholding "origin" is best-effort: securities resolve to a symbol; interest-
  derived withholding shows a type label.
