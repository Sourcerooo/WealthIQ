# Glossary

## Core Terms

- `Portfolio ledger`: the canonical ordered set of normalized portfolio facts used as the source of truth for tracking, valuation, and tax calculation
- `Portfolio entry`: one canonical ledger fact with provenance and broker-neutral semantics
- `TradeEntry`: canonical buy or sell ledger entry with quantity, unit price, fees, and taxes in source currency
- `CashEntry`: canonical cash ledger entry for dividends, interest, withholding tax, and other cash flows
- `PositionAdjustmentEntry`: canonical ledger entry for non-trade position changes such as splits or manual corrections
- `AssetTransferEntry`: canonical ledger entry for non-disposal transfers
- `OpenLot`: open position slice with provenance, remaining quantity, remaining costs, and accumulated `Vorabpauschale`
- `LotConsumption`: realized slice created when a closing trade consumes an open lot
- `TradeMatchResult`: output of the lot matcher containing consumptions, updated lots, and an optional remainder lot
- `Instrument catalog`: the imported and enriched set of instruments used by tax calculation and reporting
- `Import diagnostic`: structured message produced during import to report errors, warnings, or ignored records
- `GermanTaxEntry`: yearly reporting ledger entry used by the current German tax calculator
- `Source provenance`: metadata that links a canonical ledger entry back to the originating broker record
- `FX conversion layer`: a later calculation layer that converts ledger amounts from source currency into a chosen base currency using FX lookup data

## Tax Terms Used In The Current Implementation

- `FIFO`: default lot matching policy where the oldest eligible open lot is matched first
- `Teilfreistellungsquote`: tax-exemption ratio applied to some instruments during dividend, sell, and `Vorabpauschale` taxation
- `Vorabpauschale`: year-end advance taxation amount stored on open lots and later deducted on sale
- `Foreign withholding tax`: tax withheld at source and recorded separately in the current ledger model

## Architectural Terms

- `Domain`: core business vocabulary and value-centric models
- `Application`: orchestration, algorithms, and application-facing contracts
- `Infrastructure`: broker-specific and file-based technical adapters
- `Delivery`: the executable host and presentation logic, currently the CLI
