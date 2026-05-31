# Application Component

## Responsibility

`WealthIQ.Application` owns orchestration and application-facing contracts for the currently implemented processing pipeline.

It currently contains:

- broker-agnostic import contracts and diagnostics types
- FIFO lot matching implementation
- German tax calculation orchestration
- instrument catalog enrichment
- ports for tax reference data and instrument profiles
- orchestration from broker source records into the canonical portfolio ledger
- replay from the canonical ledger into portfolio state and tax state

## What The Application Owns

- choosing how a closing trade is matched against open lots
- processing chronological canonical portfolio entries into yearly German tax ledger entries
- aggregating imported instruments into the effective instrument catalog used for calculation
- defining the import request/result surface used by infrastructure adapters
- owning replay rules from canonical portfolio entries into derived states

## What The Application Must Not Own

- broker-specific XML traversal
- local file reading details for CSV and JSON formats
- console rendering
- transport-specific concerns such as HTTP or UI state

## Boundary Notes

- `FiFoMatcher` is currently the concrete implementation of `ILotMatcher`.
- `GermanTaxCalculator` depends on application ports for basis interest rates and year-end prices.
- Import contracts are broker-agnostic, but the current concrete implementation is only in `Infrastructure.IBKR`.
- The canonical portfolio ledger is now the stable application boundary between import adapters and downstream tracking, valuation, and tax workflows.
