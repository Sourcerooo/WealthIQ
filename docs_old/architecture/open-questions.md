# Open Architecture Questions

- What is the intended authoritative input/configuration location for the CLI outside the historical sample-data path under `data/old_project/`?
- Should the current test project continue to reference `WealthIQ.Infrastructure.IBKR` directly for regression coverage, or should infrastructure integration tests be split later?
- How should the importer architecture separate strict parsing, canonical mapping, validation, and manual-audit output once CSV and PDF sources are added?
- Which additional broker formats or brokers are in scope after the current IBKR XML path?
- Is the current default `Teilfreistellungsquote` behavior for imported instruments considered a stable rule or only a temporary fallback?
- How should missing JSON/CSV reference data be handled long term: fail fast, degrade gracefully, or support partial reporting?
- Should the current hard-coded CLI workflow remain the primary host shape, or be replaced by explicit commands later?
- Which parts of the current German tax calculation are considered in-scope and complete enough to document as stable product behavior versus current implementation behavior?
- What should the canonical asset model look like so that ETF, leveraged ETF, ETC, stock, crypto, bonds, and future asset classes can share one ledger without forcing tax rules into the wrong layer?
- How should historical portfolio valuation work when either price history or FX history is incomplete, given the requirement to fail on missing data?
- Which market-data abstractions are needed so Yahoo Finance can be the first provider without coupling the valuation core to one provider?
- How should auditability be represented so a PDF tax report can trace every number back to imported broker records and intermediate calculations?
- Where should target-versus-actual position deviation live later: portfolio analytics, strategy module, or execution/reconciliation module?
