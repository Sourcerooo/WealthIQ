# Domain Component

## Responsibility

`WealthIQ.Domain` owns the business vocabulary and core value-centric models used across the current system.

It currently contains:

- value objects such as `Money`, `Quantity`, `AccountId`, and `InstrumentId`
- canonical portfolio-ledger entry types
- open-lot and realization models
- tax result records
- enums that define the current business vocabulary

## What The Domain Owns

- representation of canonical trade, cash, adjustment, and transfer facts after normalization
- the canonical portfolio-entry types needed for multi-broker tracking, valuation, and tax replay
- open-lot state such as remaining quantity, fees, taxes, and accumulated `Vorabpauschale`
- derived realization values such as `CostBasis`, `Proceeds`, and `RealizedPnL`
- canonical identifiers and small invariants, for example `OpenLot.Consume(...)`

## What The Domain Must Not Own

- XML parsing or broker-specific field handling
- file system access
- CLI formatting or console output
- orchestration of yearly tax calculation across event streams

## Boundary Notes

- The domain does not know about IBKR XML.
- The domain does not know where instruments, prices, or basis interest rates come from.
- The domain models are designed to be reused by application services and tests.
- The earlier `AccountEvent` hierarchy was a legacy transition model and is no longer the active domain core.
