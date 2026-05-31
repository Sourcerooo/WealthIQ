# Import Pipeline Design

This document describes the currently implemented import flow in `IbkrStatementImporter`.

## Supported Sources

- Broker: `InteractiveBrokers`
- Format: `XML`
- Input path: one XML file or a directory containing `*.xml`

## Current Pipeline

```text
resolve input files
  -> read XML document
  -> iterate `Trade` nodes
  -> iterate `CashTransaction` nodes
  -> filter unsupported assets and cash types
  -> map rows to canonical portfolio entries
  -> collect/merge instruments
  -> sort entries chronologically
  -> remove detected cancellation pairs
  -> return `PortfolioLedger`, instruments, diagnostics
```

## Mapping Rules Implemented Today

- Trades map to `TradeEntry`.
- Dividend cash transactions map to `CashEntry` with `CashFlowType.Dividend`.
- Interest cash transactions map to `CashEntry` with `CashFlowType.Interest`.
- Withholding-tax cash transactions map to `CashEntry` with `CashFlowType.WithholdingTax`.
- Unsupported asset classes are ignored with diagnostics.
- Forex cash trades and forex-looking pairs are ignored with diagnostics.
- Imported amounts remain in source currency inside the canonical ledger.
- EUR conversion is deferred to a later dedicated FX conversion layer.

## Instrument Handling

- Instruments are created during import as they are encountered.
- If an ISIN exists, it becomes the stable identity basis.
- If no ISIN exists, a synthetic identity key is built from cash-related fields.
- The importer uses an MD5-based deterministic `Guid` to derive `InstrumentId` from that identity key.

## Diagnostics Behavior

- Unsupported sources return a fatal diagnostic.
- Missing input files return a fatal diagnostic.
- Missing transaction IDs and unsupported trade sides produce warnings.
- Read failures produce errors.
- Ignored assets and removed cancellation pairs produce informational diagnostics.

## Cancellation Cleanup

After parsing, the importer removes trade/cancel pairs when all of the following match:

- same instrument
- same side
- same quantity
- nearly same gross amount
- cancel record marked through `|CANCEL` in the source reference
