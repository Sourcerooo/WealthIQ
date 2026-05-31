# Phase 2 Feature Ideas – WealthIQ

Discussion starters for expanding WealthIQ beyond the tax-core foundation (Phase 1). Based on the Vision, Roadmap, and current architecture.

---

## Portfolio Valuation & Market Data

- **Current portfolio value in EUR** — Integrate market-data source (Yahoo Finance API?) to fetch current instrument prices, calculate total portfolio value and per-holding contribution. Needed for dashboard foundation.
- **Historical portfolio valuation** — Allow selection of past date, recalculate portfolio holdings and value as-of that date using closing prices. Entry point for performance analysis and tax-year snapshots.
- **Market data seeding & refresh** — Build infrastructure to load, store, and auto-refresh instrument prices (daily closing). Consider cache strategy for Yahoo Finance API rate limits.
- **Instrument enrichment pipeline** — Extend the existing reference-data seeder to populate prices, FX rates, and supplementary fields (market cap, sector, etc.) for better insight.

---

## Multi-Broker Import

- **Tastytrade CSV import** — Implement CSV import adapter for Tastytrade statements (parallel to existing IBKR XML). Map trades, dividends, interest, FX, and fees into canonical events.
- **Trader's Place PDF import** — Add PDF import adapter for Trader's Place statements. Likely the most parsing-heavy; consider OCR or structured PDF extraction library.
- **Unified import service** — Extend `ImportPipeline` to accept multiple broker sources in one batch, deduplicate on `SourceProvenance`, and produce a single canonical ledger.
- **Re-import idempotency assurance** — Ensure repeat imports of the same statement detect and skip already-imported events; validate that partial re-imports don't corrupt open-lot tracking.

---

## Dashboard Foundation

- **Portfolio overview page** — Display current holdings (security, quantity, current price, cost basis, unrealized P&L, allocation %), total portfolio value in EUR.
- **Positions and lots view** — Detailed list of open lots per holding; show age, cost per unit, current unit price, unrealized gain/loss. Link to matched/realized entries.
- **Tax summary dashboard** — Yearly view: total disposals, total gains/losses, Vorabpauschale, withholding tax. Drill-down to year's transactions, matched lots, tax entries.
- **Account/currency view** — Show holdings and activity grouped by account (if multi-account) and currency; display FX rates used in calculations.
- **Import diagnostics page** — Visible list of warnings and errors from last import (unmapped assets, missing prices, duplicate detections). Help user triage and resolve.

---

## Data Quality & Audit

- **Import diagnostic collection** — Extend the existing `ImportDiagnostic` system to capture and persist all Info/Warning/Error/Fatal outcomes from import runs. Store in DB for later inspection.
- **Audit trail from source to result** — Link calculated tax entries and realized lots back to their source `SourceProvenance` references. Clickable drill-down from dashboard report to underlying events.
- **Reconciliation helper** — Compare imported holdings against calculated portfolio state; highlight gaps (missing entries, quantity mismatches). Warn on significant divergence.
- **Data freshness indicators** — Show age of last successful import, last price refresh, any stale reference-data warnings.

---

## Tax Dashboard & Reporting UI

- **Tax year selector** — Let user choose tax year and view that year's summary, disposals, Vorabpauschale, dividends, interest, withholding tax on the dashboard.
- **Disposal details view** — List of realized lots for the year; show acquisition date/price, sale date/price, holding period, gain/loss, applicable tax rules (Teilfreistellung, tax-free threshold).
- **Vorabpauschale breakdown** — Show per-holding calculation: acquisition value, months held, Basiszins, pro-rata result, cap check, distribution deduction.
- **Tax document links** — (Future: PDF export via this view, but for Phase 2 focus on dashboard clarity.)

---

## Reference Data & FX Management

- **Historical FX rate management** — Build UI to inspect, validate, and manually upload missing or corrected FX rates (if auto-fetch fails).
- **Instrument master data** — Dashboard to view all known instruments, mark as active/inactive, edit Teilfreistellung rate, instrument type, ISIN.
- **Market data quality report** — Show instruments with missing or stale prices; flag trades that couldn't be valued.

---

## Import Workflow Improvements

- **Batch import status page** — Upload multiple files at once; see import progress, validation results, and consolidated diagnostics before committing.
- **Preview & validate before commit** — Show what the import will do (new events detected, value checks, FX lookups) before writing to DB.
- **Dry-run import** — Run import logic without persisting; inspect diagnostics and preview to ensure correctness.

---

## Considerations for Prioritization

- **Dependency order:** Market data + valuation foundation enable all dashboard features. Multi-broker adapters can proceed in parallel once import diagnostics are solid.
- **MVP scope:** For a tight Phase 2, consider focusing on **one broker** (Tastytrade CSV, smaller parsing surface than PDF), **current + historical valuation**, and **basic portfolio dashboard** (overview + positions + tax summary).
- **Fail-fast validation:** Extend diagnostics collection and make them visible early so data quality issues surface in the UI, not in late-stage reporting.
- **Test coverage:** Each new broker adapter and valuation calculation needs regression tests against real statement samples.

---

## Not included in Phase 2 (defer to later)

- PDF export (Phase 4 / Milestone 4)
- Strategy definition and evaluation (Phase 5 / Milestone 5)
- Backtesting (Phase 6 / Milestone 6)
- Broker-connected execution (Phase 7 / Milestone 7)
- Advanced portfolio analysis (drawdown, Sharpe ratio, etc.)
