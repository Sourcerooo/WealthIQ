# WealthIQ Phase 2 — Feature Ideas (Discussion Starters)

Based on the vision, roadmap, and what was delivered in phase 1 (IBKR import → canonical ledger →
German tax report in the Blazor dashboard). These are candidate topics for discussion, not commitments.

---

## A. Portfolio Positions & Allocation View

- A **"Positionen" page** showing all currently open lots derived from ledger replay:
  instrument, quantity, average cost basis, total cost (EUR), account.
- Without market data: show cost basis only, no unrealized P&L.
- Grouping by asset class (ETF, ETC, stock) and account.
- Would make the daily "what do I hold?" question answerable without opening IBKR.

---

## B. Market Data Integration (Yahoo Finance)

- Connect to Yahoo Finance (or a local CSV override) to fetch current closing prices
  and store them in SQLite (same model as historical prices already seeded).
- Keep it behind a port (`IMarketDataProvider`) so the source can be swapped later.
- A **manual refresh** trigger in the dashboard (no background scheduler needed for v2).
- Unlocks portfolio valuation and brings the existing `PortfolioValuationService` to life.

---

## C. Portfolio Valuation & Dashboard Charts

- Show current EUR value of each position using live market prices + current FX rates.
- Total portfolio value + allocation breakdown (pie or bar chart, e.g. ApexCharts via JS-Interop).
- Historical portfolio value: pick a date → replay ledger state + price + FX at that date.
- This makes phase 1's FX-correctness work visible and useful to the user.

---

## D. Tax Report UX & PDF Export

- **PDF export** of the Steuerreport: the single most requested output for actual Finanzamt use.
  Candidates: `QuestPDF` (pure .NET, very clean) or iText7 / PdfPig.
- Better **drill-down in the tax report**: clicking a realized sale opens a detail panel showing
  the matched lots, their acquisition dates, FIFO cost basis, and the source IBKR rows (provenance).
- Tax report for **multiple years** at once (already year-selectable, but a multi-year summary
  tab would be useful for planning).

---

## E. Import History & Batch Management

- A **"Imports" page**: list of all past import batches with date, file name, entry count, and
  any diagnostics. The user can see what was imported when.
- Ability to **delete a batch** (and its entries) and re-import — useful when a corrected file
  arrives from the broker.
- Shows whether a batch is clean or has warnings/errors.

---

## F. Reference Data Management UI

- Currently instruments, FX rates, prices, and Basiszins are seeded from CSV files.
- A lightweight **"Referenzdaten" admin page** to:
  - View and edit instrument metadata (ISIN, name, Teilfreistellungsquote, asset class).
  - Add missing instruments encountered during import without touching CSV files.
  - View seeded FX rates and prices; manually add a missing rate when import blocks.
- Not full CRUD for everything — focus on the gaps that cause blocking import errors.

---

## G. Second Broker Adapter (Tastytrade CSV)

- The port/adapter structure is already in place; adding a second importer is mostly
  new parsing + mapping, not new architecture.
- Tastytrade is used for options/leveraged products, so this also tests whether the
  `PositionAdjustmentEntry` / `AssetTransferEntry` paths hold up for non-IBKR events.
- Would require deciding how to model options trades in the canonical ledger first
  (currently only `STK`/`FUND` are supported).

---

## H. Diagnostics / Data Quality Improvements

- The Diagnostics page exists but is a flat list. A **data quality dashboard** would show:
  - Count of unresolved import warnings/errors by instrument or account.
  - Missing FX rates that block replay (with the date and currency pair needed).
  - Unknown ISINs that were skipped as Info diagnostics.
- Would make the "what's missing before the tax report is complete?" question answerable at a glance.

---

## Rough Priority Thinking

Most immediate value based on what's built:

1. **E (Import history)** — low complexity, makes the existing import workflow production-usable.
2. **A (Positions view)** — straight replay from the existing ledger, no new data sources needed.
3. **F (Reference data UI)** — removes friction when instruments or FX rates are missing.
4. **D (PDF export)** — concrete deliverable for actual tax filing; fits within existing tax report.
5. **B + C (Market data + valuation)** — bigger scope but high visibility; needs a clear data-source decision first.
6. **H (Diagnostics improvements)** — nice-to-have, low priority.
7. **G (Tastytrade)** — useful but depends on options-modeling decisions.
