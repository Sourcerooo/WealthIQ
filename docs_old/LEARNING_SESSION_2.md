# Learning Session 2 - Broker Import Foundation (IBKR First, Multi-Broker Ready)

## Goal
- Implement the first real data import flow using IBKR XML files from `data/input/`.
- Keep the design extensible for future brokers with CSV/PDF formats.
- Build a stable ingest pipeline: parse -> map -> diagnostics -> summary.

## Why this session matters
- You already built the core domain + FIFO matching in Session 1.
- This session connects real-world broker statements to your domain model.
- It is the key architectural pivot from pure domain coding to adapter-based integration.

## Session prerequisites and status check
- Session 1 core outcomes: mostly done (domain, matcher, tests).
- Remaining Session 1 items to close in this session:
  - Add CLI command `report positions --open` (or include equivalent minimal command output).
  - Create ADR/phase note for Session 1 if still missing.

---

## Multi-broker architecture requirements (important)

Future target: IBKR + additional brokers (CSV, PDF, possibly API exports).

Use an adapter/port model from the start:

- Define one application-level import port (interface), broker-agnostic.
- Implement broker-specific adapters in infrastructure.
- Keep all file-format specifics out of Domain and Application core logic.
- Standardize output as canonical domain events + diagnostics.

Recommended contract shape:

- `IStatementImporter`
  - `bool CanHandle(ImportSource source)`
  - `Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken ct)`
- `ImportSource` includes:
  - Broker (`IBKR`, later others)
  - Format (`Xml`, `Csv`, `Pdf`)
  - File path / stream

Note on generics:
- Prefer interface + polymorphism here over generic-heavy APIs.
- Generics are useful for reusable parser internals, but not needed for top-level orchestration.

---

## Scope for Session 2

### In scope
- IBKR XML import for:
  - Trades (`<Trade ...>`)
  - Dividends (`CashTransaction type="Dividends"`)
  - Interest (`CashTransaction type="Broker Interest Received"`)
- Robust diagnostics for unknown/invalid records.
- CLI import command with summary output.
- Integration tests against real sample files in `data/input/`.

### Out of scope
- Persistence/database writing
- Full tax model completeness
- PDF/CSV parser implementation
- API/UI integration

---

## Technical approach

### XML library strategy
- Use BCL XML tools (`System.Xml.Linq` with `XDocument`/`XElement`).
- Optional optimization later: `XmlReader` streaming for huge files.
- Avoid hand-written low-level XML parsing.

### Pipeline separation
- `IbkrFlexXmlReader`: extract raw rows from XML sections.
- `IbkrEventMapper`: map raw rows to canonical domain events.
- `ImportOrchestrator` (application): runs adapter, aggregates result.

### Error model
- Never fail entire import due to one malformed row.
- Collect diagnostics with enough context:
  - file
  - section (`Trades`, `CashTransactions`)
  - source transaction id if available
  - parse error reason

---

## Parsing and mapping rules (v1)

### Common rules
- Parse decimals with `CultureInfo.InvariantCulture`.
- Parse dates with strict formats:
  - `yyyyMMdd`
  - `yyyyMMdd;HHmmss`
- Use typed IDs in domain output.
- Keep source provenance (`SourceBroker`, `SourceReference` / transaction id).

### Trade mapping
- Source: `<Trade ...>` inside `<Trades>`.
- `buySell` -> `TradeSide`.
- `quantity` -> absolute quantity in your chosen domain convention.
- `tradePrice` -> unit price.
- `ibCommission` and `taxes` -> fees/taxes (normalize sign convention once, document it).

### Cash mapping
- Source: `<CashTransaction ...>`.
- `type="Dividends"` -> dividend event.
- `type="Broker Interest Received"` -> interest event.
- `type="Withholding Tax"`:
  - Either map to separate tax cash event type, or
  - emit diagnostic and skip (must be explicit in ADR).

---

## Implementation backlog (step-by-step)

Use this as your execution checklist. Each step should build and keep tests green.

1) **Define import contracts in Application**
- Add `ImportRequest`, `ImportSource`, `ImportResult`, `ImportDiagnostic`.
- Add `IStatementImporter` (broker-agnostic).
- Add simple importer registry/resolver (choose importer by source broker/format).

2) **Create IBKR raw DTOs in Infrastructure.IBKR**
- Add internal raw row records for trade and cash transaction rows.
- Include only fields needed for v1 mapping + diagnostics context.

3) **Implement XML reader (`IbkrFlexXmlReader`)**
- Read `<Trade>` and `<CashTransaction>` nodes.
- Extract raw rows safely (null/empty tolerant).
- Return raw model + read diagnostics.

4) **Implement mapper (`IbkrEventMapper`)**
- Map IBKR raw trades -> domain trade events.
- Map dividends/interest -> domain cash events.
- Handle sign/currency/date normalization consistently.

5) **Implement IBKR importer adapter**
- `IbkrStatementImporter : IStatementImporter`.
- Wire reader + mapper + diagnostics aggregation.
- `CanHandle(...)` for `(Broker=IBKR, Format=Xml)`.

6) **Add CLI command for import summary**
- Command: `import ibkr --file <path>`.
- Output at minimum:
  - total imported events
  - count by event type
  - diagnostics count 

7) **Write tests (unit + integration)**
- Mapper tests for key conversions and edge cases.
- Integration test with at least one real file from `data/input/`.
- Test unknown cash types -> expected diagnostic behavior.

8) **Session docs + ADR updates**
- Add `docs/adr/phase-03-ibkr-ingestion.md` from template.
- Document decisions:
  - importer interface shape
  - xml library choice
  - unknown cash type handling
  - sign normalization policy

---

## Suggested test matrix for Session 2

- `Import_IbkrXml_ParsesTradesSuccessfully`
- `Import_IbkrXml_ParsesDividendsSuccessfully`
- `Import_IbkrXml_ParsesInterestSuccessfully`
- `Import_IbkrXml_UnknownCashType_ProducesDiagnostic`
- `Import_IbkrXml_InvalidDecimal_ProducesDiagnostic`
- `Import_IbkrXml_InvalidDate_ProducesDiagnostic`
- `Import_IbkrXml_MapsBuySellToTradeSide`
- `Import_IbkrXml_PreservesSourceReference`

---

## Definition of Done (Session 2)
- At least one real IBKR XML file from `data/input/` imports successfully.
- Trades, dividends, and interest are mapped to canonical domain events.
- Diagnostics are produced for malformed/unsupported rows without crashing full import.
- CLI import summary works.
- `dotnet test` is green.
- ADR entry for Session 2 is created.

---

## Mentoring focus for this session (C#/.NET learning)
- Interface-first adapter design in .NET.
- Clean separation between Domain/Application/Infrastructure.
- Practical XML handling with BCL tools.
- Rich result objects over exception-only control flow.
- Test-driven validation with real-world fixture files.

---

## Optional stretch goals
- Add cancellation support through import stack (`CancellationToken`).
- Add `IAsyncEnumerable` import mode for streaming very large files.
- Add first stub importer for a fake CSV broker to prove pluggability.
