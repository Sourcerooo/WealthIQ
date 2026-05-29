# Infrastructure Component

## Responsibility

`WealthIQ.Infrastructure.IBKR` owns technical adapters for the current implementation.

It currently contains:

- the `IbkrStatementImporter`
- JSON-backed instrument-profile enrichment support
- CSV-backed basis-interest-rate loading
- CSV-backed year-end-price loading

## What The Infrastructure Owns

- reading XML files or directories from disk
- parsing broker-specific fields and mapping them into canonical account events
- emitting diagnostics for unsupported, invalid, or ignored source records
- loading local configuration/reference files used by tax calculation

## What The Infrastructure Must Not Own

- core lot-matching policy
- German tax calculation rules
- canonical business vocabulary definitions
- CLI presentation behavior

## Boundary Notes

- The current infrastructure layer is broker-specific by project name and implementation.
- All imported monetary values are normalized to EUR at the infrastructure boundary using source FX data.
- The current importer also performs a cancellation cleanup pass before returning final events.
