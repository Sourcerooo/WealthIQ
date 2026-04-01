# Application Contracts

This document describes the currently implemented application-facing contracts.

## Import Contracts

The import boundary is broker-agnostic at the application layer.

Import workflows now normalize into the canonical portfolio ledger defined in `docs/architecture/design/canonical-portfolio-ledger.md`.

Current core types:

- `ImportRequest`
  - `Source`
  - `AccountId`
- `ImportSource`
  - `Broker`
  - `Format`
  - `FilePath`
- `ImportResult`
  - `PortfolioLedger`
  - `Instruments`
  - `Diagnostics`
- `IStatementImporter`
  - `bool CanImport(ImportSource source)`
  - `Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken ct)`

## Legacy Transition Note

The earlier `AccountEvent` hierarchy was a transition model.
It is no longer the canonical application boundary and has been removed from the active pipeline.
New import, replay, valuation, and tax work must build on `PortfolioEntry` and `PortfolioLedger` instead.

## Tax Reference-Data Ports

The current tax calculation depends on application-facing ports rather than direct file access.

- `IBasisInterestRateProvider`
- `IYearEndPriceProvider`
- `IInstrumentProfileEnricher`

## Result Models

- `TradeMatchResult` is the result of lot matching.
- `GermanTaxCalculationResult` is the result of yearly German tax processing.
- `GermanTaxEntry` is the reporting ledger entry used by the current CLI output.

## Ledger Contract Direction

The broker-neutral contract layer is centered around:

- canonical `PortfolioEntry` types
- `PortfolioLedger`
- `SourceProvenance`
- blocking diagnostics for unmapped or ambiguous source records
- replay-oriented query/use-case boundaries for tracking, valuation, and tax processing

## Boundary Rules

- Application contracts must stay free of XML, JSON, CSV, or console-specific details.
- Infrastructure adapters implement the file and broker specifics behind these contracts.
- Broker-specific raw DTOs must not become the de facto shared model.
