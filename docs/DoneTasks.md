# Done Tasks -- WealthIQ

Completed tasks moved from `docs/Todo.md`.

| ID | Status | Task | Result | Reference |
|---|---|---|---|---|
| T003 | Done | Design canonical portfolio ledger beyond the current tax slice | The canonical ledger direction, entry families, provenance rules, currency requirements, replay rules, and ownership boundaries were documented | `docs/architecture/design/canonical-portfolio-ledger.md` |
| T013 | Done | Specify canonical ledger entry taxonomy in implementation detail | Concrete domain enums and types for canonical ledger entries, provenance, conversion metadata, and initial invariants were added to the codebase together with focused domain tests | `docs/architecture/design/canonical-portfolio-ledger.md`, `src/WealthIQ.Domain/Model/Ledger/PortfolioEntry.cs` |
| T015 | Done | Migrate from the legacy `AccountEvent` model to the canonical ledger | `AccountEvent` was documented as legacy, import now produces `PortfolioLedger`, FIFO matching and tax calculation replay from `PortfolioEntry`, and the old event hierarchy was removed from the active code path | `docs/architecture/design/canonical-portfolio-ledger.md`, `src/WealthIQ.Infrastructure.IBKR/Import/IbkrStatementImporter.cs`, `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs` |
| T016 | Done | Integrate canonical ledger into import result contracts | `ImportResult` now exposes `PortfolioLedger` as the canonical import output consumed by CLI and application services | `docs/architecture/design/application-contracts.md`, `src/WealthIQ.Application/Import/ImportResult.cs` |
