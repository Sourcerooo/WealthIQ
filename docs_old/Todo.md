# TODO -- WealthIQ

Working document for concrete next implementation tasks.

Longer-term goals belong in `docs/Vision.md`.
Milestone-level sequencing belongs in `docs/Roadmap.md`.
Completed tasks should be moved to `docs/DoneTasks.md` once this list starts being maintained over time.

**Status legend:** `Open` · `In Progress` · `Blocked` · `Done`

## Prioritization Rules

- Only add tasks whose next implementation step is already clear.
- Prefer thin vertical slices over broad parallel foundations.
- Keep tracking and tax-reporting work ahead of strategy and backtesting work.
- Treat correctness, auditability, and fail-fast behavior as first-class requirements.

## Next Up

| ID | Status | Task | Concrete next step | Reference |
|---|---|---|---|---|
| T001 | Open | Define authoritative product scope for the first usable release | Document which asset classes, tax rules, brokers, and report outputs must be fully supported in the first production-worthy slice | `docs/Vision.md`, `docs/Roadmap.md` |
| T002 | Open | Formalize fail-fast import and calculation rules | Specify which conditions must abort processing, how unmapped broker records are surfaced, and where diagnostics become blocking errors | `docs/Vision.md`, `docs/architecture/design/import-pipeline.md` |
| T004 | Open | Design multi-broker import architecture | Define the adapter structure for IBKR XML, Tastytrade CSV, and Trader's Place PDF with explicit validation and audit behavior | `docs/Roadmap.md`, `docs/architecture/design/import-pipeline.md` |
| T006 | Open | Clarify German tax scope for the first trustworthy report | List the exact rules that must be supported in the first tax-reporting release, including asset-class-specific holding-period exemptions where relevant | `docs/Vision.md`, `docs/architecture/design/german-tax-calculation.md` |
| T014 | Open | Define replay boundaries from ledger to derived states | Specify which application services rebuild positions, cash balances, historical snapshots, and tax inputs from the canonical ledger | `docs/architecture/design/canonical-portfolio-ledger.md`, `docs/architecture/design/application-contracts.md` |
| T019 | Open | Generalize FX conversion beyond `currency -> EUR` CSV lookup | Evolve the current FX contracts so later providers and alternate base currencies can be introduced without rewriting tax and valuation workflows | `docs/architecture/design/fx-conversion-layer.md`, `docs/Vision.md` |
| T021 | Open | Integrate valuation snapshots into CLI and future dashboard views | Expose current and historical portfolio valuation through a user-facing workflow on top of `PortfolioValuationService` | `docs/architecture/design/portfolio-valuation.md`, `docs/Roadmap.md` |
| T022 | Open | Expand market-data mapping and provider coverage | Add more mapped instruments and prepare the path from CSV/Yahoo download to a live market-data provider | `docs/architecture/design/portfolio-valuation.md`, `docs/Vision.md` |

## Shortly After

| ID | Status | Task | Concrete next step | Reference |
|---|---|---|---|---|
| T007 | Open | Define dashboard MVP | Decide which views are required first for portfolio overview, open positions, tax summary, and audit visibility | `docs/Vision.md`, `docs/Roadmap.md` |
| T008 | Open | Define PDF reporting MVP | Specify the first portfolio and tax PDF report structures, sections, and traceability requirements | `docs/Vision.md`, `docs/Roadmap.md` |
| T009 | Open | Define currency-conversion policy | Describe how trade-time conversion, valuation-time conversion, and historical FX lookup should work consistently in EUR | `docs/Vision.md`, `docs/Roadmap.md` |

## Later, Not Immediate

| ID | Status | Task | Why later | Reference |
|---|---|---|---|---|
| T010 | Open | Strategy-definition model | Tracking and tax foundations must be trustworthy first | `docs/Vision.md`, `docs/Roadmap.md` |
| T011 | Open | Backtesting architecture | Requires historical market data, strategy model, and portfolio analytics foundation | `docs/Vision.md`, `docs/Roadmap.md` |
| T012 | Open | Broker-connected execution support | Requires stable recommendation logic and broker integration boundaries | `docs/Vision.md`, `docs/Roadmap.md` |
