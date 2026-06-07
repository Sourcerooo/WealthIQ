# Portfolio Dashboard ("Mein Portfolio") — Design

> Date: 2026-06-07
> Status: Design approved (brainstorming complete) — ready for implementation planning.
> Phase: First feature built **on top of** the shipped v1 (IBKR + Trader's Place import → canonical ledger → German tax report). v1/tax is done, tested, and reliable; this phase extends a stable product.

## 1. Goal & context

Give the user a single, visually appealing screen that answers **"what do I own, what is it worth, and how has it changed since I bought it?"** without logging into IBKR or Trader's Place.

This is **Pillar 1** of a larger portfolio-dashboard vision. The vision has three pillars; only Pillar 1 (plus cheap visual KPIs) is in scope here:

1. **Holdings & valuation** (this phase) — current positions, EUR worth, unrealized P&L since purchase, asset-class allocation, YTD KPIs, manual price refresh, master–detail price chart.
2. **Net worth over time** (later phase) — portfolio/net-worth development over time, including cash positions. *Deferred.*
3. **Rebalancing / target positions** (later phase) — input target holdings, compute buy/sell deltas, later strategy-driven targets. *Deferred.*

### Existing scaffolding this builds on

- **`PortfolioValuationService`** (`src/WealthIQ.Application/Valuation/`) already exists with unit tests but is **dormant**: not registered in DI, not referenced by any production code (only its own test + `*Ideas.md`/`docs_old` notes). It replays the canonical ledger via `FiFoMatcher` to produce open lots, market values (price × qty, FX-converted to EUR), and cash balances as of a date. Because nothing depends on it yet, extending it is low-risk.
- **Market data:** `IHistoricalPriceLookup` / `DbHistoricalPriceLookup` (latest-on-or-before lookups over stored `HistoricalPrice` bars), `IInstrumentMarketDataMap` ((ISIN, Currency) → provider symbol), `AdjustedPriceCalculator`.
- **FX:** `IFxRateLookup` / `DbFxRateLookup` + `FxConverter` (EUR base; FX at event time).
- **Refresh:** `HistoricalPriceRefreshService.RefreshAsync(asOf, forceFullReload, ct)` → `DataRefreshResult` (already powers Marktdaten's Yahoo refresh).
- **UI:** `LightweightChart.razor` (TradingView Lightweight Charts v4, candlestick/line, dark/light sync, `InitialRangeDays`), `StatCard`, `SectionCard`, `PageHeader`, `ChartSelectionState` (per-circuit remembered chart selection), the "Midnight Ledger" dark emerald-on-navy theme.

## 2. Architecture & layering

Keeps the project's strict dependency rules (`Application → Domain`, `Web → everything`, only `Web` references `Infrastructure`).

- **`Application/Valuation/PortfolioValuationService`** remains *the* single source of valuation truth. It is **extended** (Approach 2 from brainstorming) to compute, in addition to market value, the **cost basis, average buy price, and unrealized P&L** per position. This is genuinely valuation logic and belongs in one place; the dormant status makes extension safe.
- **A thin dashboard composition** in `Application` performs work that is *presentation orchestration*, not valuation per se: ISIN aggregation + the "Alle" rollup, YTD dividends, YTD realized P&L, and the chart instrument catalog. This avoids bloating the valuation core while keeping logic out of `Web`.
- **`Web/Components/Pages/Dashboard/`** hosts the new page. It composes the above services and renders only. It reuses shared components and `HistoricalPriceRefreshService` for the refresh button.

## 3. Data model

### 3.1 Extended `PortfolioPositionSnapshot`

Per `(AccountId, InstrumentId, Direction)`, the snapshot grows from market-value-only to also carry:

| Field | Meaning |
|---|---|
| `Quantity` | remaining held quantity |
| `AverageBuyPriceNative` + `PriceCurrency` | avg buy price in the instrument's trade currency (single-currency contexts only) |
| `AverageBuyPriceEur` | `CostBasisEur / Quantity` — always EUR-derived |
| `CostBasisEur` | Σ remaining-open-lot cost (unit price + allocated fees), each lot converted with **FX at that trade's own date** |
| `ClosePrice` + `MarketValueEur` | current price and FX-converted market value (already present) |
| `UnrealizedPnlEur` | `MarketValueEur − CostBasisEur` |
| `UnrealizedPnlPct` | `UnrealizedPnlEur / CostBasisEur` |
| `AssetClass` | from `InstrumentProfile.Type`, drives the allocation donut |
| `EffectivePriceDate` | date of the price bar actually used |
| `PriceMissing` | true when no usable price bar exists |

**Average buy price rule:** always derived from EUR totals (`CostBasisEur / Quantity`). Raw prices are **never** averaged across currencies. Native average price is shown only where a single currency applies.

### 3.2 ISIN rollup ("Alle" view)

A composition step aggregates extended snapshots by **ISIN**:
- `Quantity` = Σ quantity (same ISIN across accounts/currencies)
- `CostBasisEur` = Σ cost basis EUR
- `MarketValueEur` = Σ market value EUR
- blended `AverageBuyPriceEur` = ΣcostEur / Σqty
- `UnrealizedPnlEur` / `Pct` recomputed from the EUR sums

Per-account view shows the un-aggregated snapshots (one row per account+instrument).

## 4. Page layout (the "C · Master–detail" direction)

New page at `/` (`Dashboard.razor`). Top to bottom, with `PageHeader` "Mein Portfolio":

1. **Control row:** account selector (`Alle` + each account that has holdings; `Alle` preselected) · `↻ Kurse aktualisieren` button · "Kurse per `<date>`" freshness chip (subtle warning style when the latest price date is more than 4 calendar days old — covers a normal weekend plus a holiday).
2. **Hero row:** allocation donut **by asset class** with total securities value in the centre (left) + KPI cards (right).
3. **Master–detail row:**
   - **Left — Positionen table** grouped by ISIN: Instrument (symbol + ISIN) · Menge · Ø Kauf · Kurs · Wert € · +/− € · +/− %. Gains green / losses red, tabular numerals. **Clicking a row selects it for the chart.**
   - **Right — Kursverlauf panel:** compact `LightweightChart` (candlestick) with 1M/6M/1J range toggle and an **instrument dropdown** (autocomplete over **all instruments with price data**, including ones not held — for eyeballing candidate buys). Row-click sets the chart; the dropdown overrides. A **held** position additionally gets a dashed average-buy reference line; a not-held candidate just shows price. Selection persists across navigation via the existing `ChartSelectionState` pattern.

Reuses existing shared components and theme. `Components/Shared/` components take an outer wrapper `div` for spacing (no `Class`/`Style` params; icon-only buttons use `aria-label`).

### Allocation breakdown

Only **by asset class** for this phase (`InstrumentProfile.Type`). The chart/aggregation are written so other breakdowns (instrument, account, currency) could be added later, but they are not built now.

## 5. KPIs

KPI cards (`StatCard`):

- **Gesamtwert (Wertpapiere)** — Σ securities market value EUR. Explicitly labelled "Wertpapiere" because cash is excluded this phase.
- **Nicht realisierter G/V** — € and %.
- **Dividenden `<year>`** — YTD, from ledger dividend cash entries converted to EUR (FX at event time).
- **Realisiert `<year>`** — YTD **gross** realized gain (proceeds − cost basis, EUR, derived via FIFO). This is an investment metric, **not** a taxable figure — no Teilfreistellung/Vorabpauschale adjustments (those stay in the tax engine/Steuerreport). *Confirmed in scope.*
- *(small)* **Positionen / Konten** counts.

The displayed year is the current calendar year.

## 6. Cash handling

Cash is **excluded** from this phase. `Gesamtwert` is securities-only and labelled accordingly, so the number is honest. The valuation service already computes cash balances; they are simply not surfaced. Full net worth including cash is **Pillar 2**.

## 7. Missing-data behaviour — resilient, not fail-fast

Unlike the tax engine (which must block on missing price/FX/profile data), the **dashboard must still render**:

- A position with **no usable price** shows "Kurs fehlt", is visibly flagged, and is **excluded from totals and the allocation donut**, with a small note (e.g. "1 Position ohne Kurs").
- A position with **no asset class** is grouped under "Sonstige" in the donut.
- A missing FX rate for an otherwise-valued position is treated the same way (flagged, excluded from totals) rather than throwing.

A single data gap must never blank the whole page.

## 8. Routing, navigation & DI

- **`/` → `Dashboard.razor`** (new landing page).
- The current Steuerreport (today at `/`) **moves to `/steuerreport`**, still under the **Bericht** nav group.
- **Nav:** new top-pinned **Dashboard / Mein Portfolio** entry above Bericht. `FocusOnNavigate Selector="h1"` continues to work via `PageHeader`.
- **DI (`Program.cs`):** register `PortfolioValuationService` and the dashboard composition service (both `Scoped`, consistent with existing report/lookup registrations).

## 9. Testing

Deterministic, no live network, fixed reference data (per project conventions).

- **`PortfolioValuationServiceTests` (extended):** cost basis, average buy price (EUR), unrealized P&L (€ and %), **mixed-currency same-ISIN** aggregation correctness, missing-price flagging.
- **Dashboard composition tests:** ISIN "Alle" rollup (quantity/cost/value sums, blended avg buy price), dividends-YTD, realized-YTD.
- **Manual smoke test** via `dotnet run` (Blazor render errors don't surface in build/xUnit): page renders, account switch re-filters, row-click → chart updates, dropdown → non-held instrument loads, refresh button runs, and a deliberately missing price renders the resilient "Kurs fehlt" path.
- No live Yahoo/ECB calls in tests; the refresh button is exercised manually only.

## 10. Documentation

Update `CLAUDE.md`:
- Reframe v1 as a stable, shipped product being extended (rather than "in progress").
- Move **portfolio valuation/charts** out of the "out of v1 scope" list into the delivered feature set; describe the new `/` dashboard, the extended `PortfolioValuationService` surface, the resilient (non-fail-fast) display stance for the dashboard, and the new `/steuerreport` route.
- Record **Pillar 2 (net worth over time, incl. cash)** and **Pillar 3 (rebalancing / target positions)** as planned follow-up phases so scope stays clear.

## 11. Out of scope (named to prevent drift)

- Value-over-time / net-worth time-series charts and cash-inclusive net worth (Pillar 2).
- Target-position input, buy/sell delta recommendations, strategy-driven targets (Pillar 3).
- Allocation breakdowns other than asset class.
- Background/scheduled price refresh (manual button only).
- PDF export, multi-base-currency, additional brokers.
