# OpusIdeas.md — Phase 2 feature discussion starters

> Status: **discussion starters only** — not a spec or implementation plan. Bullets to argue over before we brainstorm/scope Phase 2.
> Grounded in `docs_old/Vision.md`, `docs_old/Roadmap.md`, `docs_old/Todo.md`, and the current post-Plan-1–3 codebase.

## Where we are after Phase 1 (v1)
- Canonical ledger + FIFO + `GermanTaxCalculator` (disposals, Vorabpauschale, Teilfreistellung, dividends/interest/withholding) — results matching the real Finanzamt figures.
- IBKR XML import → SQLite (EF Core) → Blazor dashboard with Steuerreport, Import, and Diagnostics/Audit pages.
- Reference-data seeding (`data/reference/`), FX-at-event-time rule, fail-fast diagnostics.
- This maps to Roadmap **Milestone 1 (Trusted Tracking & Tax Core)** — essentially complete.

### Already-present scaffolding worth knowing (changes the cost of some ideas)
- **Valuation logic already exists but is not surfaced in the UI:** `WealthIQ.Application/Valuation/PortfolioValuationService` + `PortfolioValuationSnapshot`/`PortfolioPositionSnapshot`/`PortfolioCashSnapshot`, with tests.
- **Market-data ports already exist:** `MarketData/IHistoricalPriceLookup`, `IInstrumentMarketDataMap`, `PriceBar`, `PriceLookupDateHandling`; plus `Infrastructure/Ibkr/MarketData/CsvHistoricalPriceLookup` + `JsonInstrumentMarketDataMap`.
- **Data + tooling already exist:** seed `historical_prices.csv` and `market_data_mappings.json` in `data/reference/`; Python scripts `download_price_history.py` (Yahoo) and `download_fx_rates.py` (ECB).
- Implication: **portfolio valuation is mostly "wire up + surface + harden", not "build from scratch."**

---

## Candidate Phase-2 themes (discussion starters)

### A. Make the portfolio visible — valuation & dashboard (Roadmap M3 / Release Theme B)  ⭐ strongest anchor
- Wire the existing `PortfolioValuationService` into a new dashboard page: current holdings, EUR value, allocation by asset class / instrument.
- Historical valuation: pick a past date → day-level closing-price portfolio value (vision explicitly says day-level is enough).
- First KPIs: open-position unrealized PnL, realized PnL (already in tax engine), per-position cost basis, allocation %.
- Promote market data from CSV seed to a live provider behind the existing port (Yahoo via the existing script, or a C# `IHistoricalPriceLookup` adapter) — keep fail-fast on missing price/FX.
- Effort: **M** (logic exists). Risk: multi-currency valuation correctness; missing-price handling. Highest visible payoff for least new code.

### B. Multi-broker ingestion (Roadmap M2 / Release Theme A)
- Second importer behind the existing `IStatementImporter` port: **Tastytrade CSV** (much easier than PDF).
- **Trader's Place PDF** — flagged in roadmap as a disproportionate complexity driver; likely *defer* or spike separately.
- Unify all brokers into the one canonical ledger; dedup/idempotency already exists via `SourceProvenance`.
- Effort: Tastytrade CSV **M**, PDF **L**. Risk: PDF parsing reliability. Good "prove the ports generalize" win.

### C. Extend tax scope for current asset mix
- **Crypto & gold (ETC) one-year holding-period exemption** (§23 private sales) — the portfolio profile is 10% gold ETC + crypto ambitions; the vision calls this out explicitly. Extends the existing tax engine rather than adding a new subsystem.
- Make every unassignable/unmapped event a visible, blocking audit path (vision non-negotiable) — partly done via diagnostics; tighten coverage.
- Effort: **S–M**. Risk: tax-rule scope creep (roadmap warns about this). Needs a clear "what's in scope" decision before coding.

### D. Professional reporting / PDF (Roadmap M4 / Release Theme C)
- PDF export of the German tax report (the actual spreadsheet-replacement payoff).
- Audit trail from report line → source statement record, rendered into the export.
- Effort: **M–L** (pick a .NET PDF lib; layout work). Risk: scope of "Finanzamt-grade" formatting. Best done *after* the dashboard tax views are stable.

### E. Foundation / correctness hardening (cross-cutting, enables everything above)
- Close the two known thin spots from the v1 notes: **short-position tax semantics** and **Teilfreistellung variants other than 30%/0%**.
- Generalize FX contracts so a non-EUR base currency / alternate providers are possible later without rewriting tax + valuation (Todo T019).
- Reconciliation check: broker-reported holdings vs. ledger-derived positions → data-quality dashboard (vision "candidate future features").
- Effort: **S–M each**. Low glamour, high trust dividend.

### Explicitly later (out of Phase 2 — name them so we don't drift)
- Strategy definition & recommendation engine (M5), backtesting (M6), broker-API execution (M7). All require the valuation + analytics foundation first.

---

## A possible "good and manageable" Phase 2 (my recommendation to react to)
1. **Theme A (valuation + dashboard)** as the headline — leverages existing code, delivers visible value.
2. **One slice of Theme B** — Tastytrade CSV importer only (defer PDF) — proves multi-broker without the PDF rabbit hole.
3. **Theme C: crypto/gold holding-period exemption** — small, high-relevance tax extension on top of the engine we trust.
4. Fold in **Theme E hardening** opportunistically (short positions, FX generalization) as these themes touch the code.
- Deliberately **defer**: PDF import (B) and PDF export (D) to a Phase 2.5/3 once the dashboard valuation views exist.

## Open questions to settle before scoping
- Live market data now, or stay on CSV/downloaded snapshots for Phase 2? (Affects determinism, testing, and the fail-fast story.)
- Is the next broker actually Tastytrade, or is Trader's Place PDF the real pain point worth attacking?
- How far do we push tax scope (crypto/gold §23) before a professional tax review?
- Dashboard depth: read-only valuation views first, or also make reference data UI-editable (spec hinted at this)?
- PDF: is dashboard-on-screen enough for the next tax season, or is the PDF the actual must-have?
