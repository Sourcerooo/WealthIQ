# Phase 3 Streamlining — Design

**Date:** 2026-06-06
**Status:** Approved (design), pending implementation plan
**Scope:** Refinements to the Phase 3 Data Visualization UI after manual testing. UI-focused, with one change reaching into Domain/Application (item 9).

## Context

Phase 3 added the Data Browser (Ledger, Kurschart, Wechselkurse), Lightweight Charts, an inline-editable Basiszins table, and Steuerreport drill-down links. After testing, ten streamlining items were identified. This spec covers all ten, grouped by the page they touch.

Authoritative existing files:
- Ledger browser: `src/WealthIQ.Web/Components/Pages/Browse/LedgerBrowser.razor`
- Kurschart: `src/WealthIQ.Web/Components/Pages/Browse/PriceChart.razor`
- Wechselkurse browser: `src/WealthIQ.Web/Components/Pages/Browse/FxChart.razor`
- Marktdaten: `src/WealthIQ.Web/Components/Pages/DataAdmin.razor`
- Steuerreport: `src/WealthIQ.Web/Components/Pages/Steuerreport.razor`
- Tax entry type: `src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs`
- Tax calculator: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs`
- FX provider/refresh: `src/WealthIQ.Application/Currency/` + `src/WealthIQ.Infrastructure/Ibkr/Currency/EcbFxRateProvider.cs`
- Charts JS interop: `src/WealthIQ.Web/wwwroot/wiq-charts.js`; count-up/scroll helpers: `src/WealthIQ.Web/wwwroot/wealthiq.js`
- Nav: `src/WealthIQ.Web/Components/Layout/MainLayout.razor`

## Architecture constraints (unchanged, must hold)

- Canonical ledger remains source of truth; tax is replayed. Item 9 only **surfaces** existing replay data plus already-available source provenance — it changes no tax math.
- The golden regression baseline (`GermanTaxRegressionTests`) asserts exact 2024 figures. Item 9 adds **additive, optional, display-only** fields to `GermanTaxEntry`; existing positional construction and computed figures are unchanged, so the baseline stays green without edited expected values.
- Adjusted OHLC stays display-only; the tax engine still uses raw `Close`.
- Dependency direction unchanged: Domain ← Application ← Infrastructure ← Web.

---

## A. Ledger screen (`/browse/ledger`) — items 1 & 2

### 1. Account selector + grouping
- Add a `MudSelect<string>` (or account-id-keyed select) to the `PageHeader` `Actions`, listing every account that has at least one ledger entry, labelled by `Account.AccountNumber`.
- Preselect the **first** account on load. The kind-split tables (Trades / Dividenden / Zinsen / Quellensteuer / Sonstige) show **only the selected account's** entries.
- No "Alle Konten" option (single-user tool; one account at a time). Source: `PortfolioEntry.AccountId`; account names from `ledger.Accounts` (`LedgerStore.LoadLedgerAsync`).
- Implementation: filter the already-loaded entries by `AccountId` in the component; switching account re-filters in memory (no reload).

### 2. Move ledger deletion here
- Add to this page: the "Ledger leeren (unwiderruflich)" button + the "Rohdateien (Audit) löschen" checkbox, with the same confirmation dialog currently in `DataAdmin`.
- Inject `ILedgerClearService` and `IDialogService`. After a successful clear, reload the ledger and refresh the account list.
- Placement: a small action area on the page (e.g. a `SectionCard` or a footer row) — not in the `PageHeader` (the header holds the account select). Confirmation copy reused verbatim from `DataAdmin.ClearLedger`.

---

## B. Marktdaten (`/data-admin`) cleanup — items 2, 3, 6, 7

### 2 & 7. Remove panels
- Remove the **Ledger (Buchungen)** expansion panel entirely (moved to the Ledger screen).
- Remove the **Instrumente** expansion panel entirely (it has its own nav entry `/data-admin/instruments`).
- Remove now-unused fields/injections from `DataAdmin` (`_ledgerEntries/_accounts/_batches`, `_profileCount/_listingCount`, `_purgeAuditFiles`, `ILedgerClearService`, ledger/instrument status queries in `LoadStatusAsync`).

### 3. FX panel: incremental update + add currency + backfill
- **Incremental button** "Inkrementell aktualisieren": fetches from `max(stored Date)+1 … today` for the currently-tracked currency set. Mirrors `HistoricalPriceRefreshService.RefreshAsync` semantics.
- **Add currency**: a dropdown of the full set of ECB-published currency codes, plus a "Hinzufügen + Backfill" action that fetches the chosen currency's **full available history** and upserts it.
- **Tracked currency set** is derived dynamically: `distinct currencies present in FxRates ∪ { USD, GBP, CHF }` (the seed defaults). Adding a currency is therefore self-persisting — once it has rows, incremental refresh includes it automatically. No new settings table.
- **Provider change**: `IFxRateProvider.FetchAsync(from, to, ct)` gains a currency filter, e.g. `FetchAsync(from, to, IReadOnlyCollection<string>? currencies, ct)`. `EcbFxRateProvider` filters by the passed set instead of the fixed `options.SupportedCurrencies` (the options list becomes the default when no set is passed). The ECB historical XML contains the full currency universe, so backfilling any ECB currency needs no new data source.
- **Refresh service change**: `FxRateRefreshService` gains:
  - `RefreshIncrementalAsync(DateOnly asOf, CancellationToken)` — computes per-set start = max stored date + 1.
  - `AddCurrencyAsync(string currency, DateOnly from, DateOnly to, CancellationToken)` — backfills one currency over a wide range.
  - The store/provider must expose the max stored date (add to `IFxRateStore` or query in the service via a new store method). Keep the existing `RefreshAsync(from, to)` for the explicit-range button.
- **ECB currency list** for the dropdown: a static known-ECB-codes constant (deterministic; no live fetch needed just to populate a dropdown). Already-tracked currencies may be excluded from the "add" dropdown.

### 6. Basiszins panel: single editable table
- Collapse to one table with:
  - inline row edit of the rate (existing `RowEditCommit` → `BasisInterestRateRefreshService.SetManualAsync`),
  - per-row delete (existing `DeleteAsync`),
  - an inline **add-row** input group ("Jahr" numeric + "Zinssatz" numeric + "Speichern") that calls `SetManualAsync` then reloads.
- After any add/edit, the list is **sorted by year** (ascending). (Current code sorts descending; switch to ascending per the "always sort by year" intent — confirm direction during implementation; ascending chosen as default.)
- Remove: the "Von BMF abrufen" button, the standalone top "Jahr" `_basisYear` field, and the separate "Manuell erfassen" section heading/divider (the add-row replaces it).
- `RefreshBasiszins`/`_basisYear` and the BMF provider call are removed from this page. (The `BasisInterestRateRefreshService` BMF method may remain in code but is no longer surfaced in the UI.)

---

## C. Kurschart (`/browse/prices`) — items 4 & 5

### 4. Dropdown UX + preselect + remember
- Set `Clearable="false"` on the `MudAutocomplete` (removes the "x"; keeps type-to-search; fixes the "must deselect before reselect" problem).
- **Preselect**: on first load, if nothing is remembered, select the first listing (alphabetical by `ProviderSymbol`) and load its candles.
- **Remember across pages**: introduce a **scoped in-memory service** `ChartSelectionState` (DI `AddScoped`) holding the selected `ProviderSymbol`. Persists within the Blazor Server circuit (survives navigation between pages), resets on full reload. On init, restore from the service if set; on change, write to the service.

### 5. Zoom to last year
- After candles are loaded/rendered, set the chart's visible range to the **last ~365 days** (from `lastDate − 365d` to `lastDate`) using Lightweight Charts `timeScale().setVisibleRange({ from, to })`.
- Implemented in `wiq-charts.js` (global `window.wiqCharts`): the candlestick setup applies the initial visible range after `setData`. Scoped to candlestick / Kurschart only — the FX line chart is unchanged. If a series has fewer than ~365 days of data, fall back to `fitContent()`.

---

## D. Steuerreport (`/`) — items 8, 9, 10

### 8. Verkäufe → Verkäufe-Details link: scroll + highlight
- Keep both the "Verkäufe" summary and "Verkäufe — Details" tables.
- "Anzeigen" continues to scroll to the matching detail row (same `rowList.IndexOf(row)` index → `sell-detail-{index}` anchor), and additionally **flash-highlights** the target row.
- Extend the JS helper (`wealthiq.scrollToAnchor` or a new `wealthiq.scrollAndHighlight`) to add a transient CSS class (e.g. `wiq-row-highlight`) to the target row, removed after the animation. Add the `.wiq-row-highlight` keyframe to `wealthiq.css` (honor `prefers-reduced-motion` — degrade to a static brief background with no animation).

### 9. Inline source/explanation expand (replaces navigating links)
Replace the "Quelle/Import/Anzeigen" navigating buttons in **Verkäufe — Details, Dividenden, Quellensteuer, Vorabpauschale** with an **expandable detail row** (MudTable nested/expand pattern, or a toggle button revealing a detail `<tr>`) showing the origin **in place** — no navigation.

Content per type:
- **Dividenden / Quellensteuer / Zinsen** (cash-derived): broker reference (`SourceProvenance.SourceRecordReference`), source file (`SourceProvenance.SourceLocation`), original-currency gross amount + currency, and event date.
- **Verkäufe — Details** (FIFO consumption): open-trade reference + close-trade reference + source file. (Open ref already tracked on the lot as `OpenSourceReference`; close ref = `tradeEntry.SourceProvenance.SourceRecordReference`.)
- **Vorabpauschale** (synthetic — no single source entry): the calculation inputs — Jahresanfangskurs (year-start redemption price, EUR), Jahresendkurs (year-end, EUR), Basiszins (rate), gehaltene Stück, Ausschüttung/Anteil, Monatsfaktor.

**Domain/Application changes** (additive, display-only):
- `GermanTaxEntry` gains optional fields, e.g.:
  - `SourceReference` (string), `SourceFile` (string), `OriginalAmount` (decimal), `OriginalCurrency` (string) — for cash + sell entries (sell may carry the close-trade reference; open reference can reuse `Origin`/a dedicated field).
  - Vorab inputs: `YearStartPrice`, `YearEndPrice`, `BasisRate`, `HeldQuantity`, `DistributionPerShare`, `MonthFactor` (decimals).
  - All optional with defaults → existing positional construction unaffected; regression figures unchanged.
- `GermanTaxCalculator` populates the new fields at the existing `new GermanTaxEntry(...)` sites (cash at lines ~154/184/206; sell at ~115; Vorab at ~311). All values are already in scope at those points.
- The Web `Steuerreport` tables render the expand using these fields; the `DrillToSource`/`/audit?isin=` navigation is removed from these tables. (The Ledger browser and Audit page remain for free-form exploration.)

### 10. Remove "Verrechnete Vorabpauschale" column where it's always 0
- Make the "Verrechn. Vorabpausch." column **conditional** in the shared `EntryTable` render fragment (new `showVorab` flag).
- Show it **only for Verkäufe** (consumed Vorab is meaningful there). Hide it for **Dividenden, Zinsen, and the Vorabpauschale table** (always 0 by tax law).

---

## Testing

- **Tax calculator (item 9):** add/extend a unit test asserting the new display fields are populated correctly for a sell, a dividend, a withholding, and a Vorabpauschale entry. Confirm `GermanTaxRegressionTests` stays green unchanged (figures untouched).
- **FX refresh (item 3):** unit tests for `RefreshIncrementalAsync` (start = max+1) and `AddCurrencyAsync` (fetches a non-default currency); `EcbFxRateProvider` filters by the passed currency set. Keep deterministic (fake provider/in-memory store; no live network).
- **Basiszins (item 6):** existing service tests cover `SetManualAsync`/`DeleteAsync`; add a small sort-order assertion if a view-model helper is introduced.
- **UI items (1, 2, 4, 5, 8):** verified by manual `dotnet run` smoke test (Blazor render correctness isn't covered by build/xUnit — per project memory). Checklist: account switch filters the ledger; ledger delete works from the new location; Marktdaten no longer shows Ledger/Instrumente panels; Kurschart preselects, remembers across nav, no "x", opens zoomed to last year; Steuerreport "Anzeigen" highlights the row; inline expands show correct origin/inputs; the Vorab column is gone from Dividenden/Zinsen.

## Out of scope

- No change to tax math, FIFO matching, or replay ordering.
- No new broker/data sources (ECB XML already carries all currencies).
- FX line chart zoom (item 5 is Kurschart-only).
- "Alle Konten" aggregate view on the Ledger screen.
- Retaining a BMF auto-fetch button in the Basiszins UI.
