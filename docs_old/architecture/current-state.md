# Current Architecture State

This document summarizes the current implementation maturity of the architecture.
It is informative and should not replace `docs/Todo.md` once a concrete task list exists.

## Layer Snapshot

| Layer | State | Notes |
|---|---|---|
| Domain | Implemented and used | Core ledger, lot, instrument, and tax-entry models exist and are covered by focused tests |
| Application | Implemented for the current slice | FIFO matching, ledger-based tax calculation, FX conversion, import contracts, and instrument enrichment exist |
| Infrastructure | Implemented for one broker path | IBKR XML import plus file-based JSON/CSV reference data adapters exist |
| CLI | Implemented and usable | One hard-coded tax-report workflow exists; there is no command parser yet |
| Testing | Implemented | Domain tests, application tests, and one end-to-end regression test exist |
| API / UI / Persistence | Not present in the current implementation | No current projects or contracts exist in `src/` |

## Component Snapshot

| Component | State | Main gap |
|---|---|---|
| Canonical portfolio ledger | Implemented as the active core model | Broader replay services beyond tax and matching are still missing |
| Portfolio valuation foundation | Implemented | CLI/dashboard integration and broader asset/report views are still missing |
| Lot matching | Implemented | No alternative policies beyond FIFO |
| German tax calculation | Migrated to ledger replay | Tax scope completeness beyond the currently supported rules still needs definition |
| FX conversion layer | Implemented for current tax and CLI needs | Only `currency -> EUR` CSV lookup is implemented so far |
| IBKR import | Implemented | No dedicated importer test suite yet |
| CLI reporting | Implemented | No command routing or broader workflow surface |
| Reference-data loading | Implemented | Runtime defaults and asset packaging are not yet documented as stable contracts |

## Implemented Capability Snapshot

### Domain And Application

| Capability | State | Notes |
|---|---|---|
| Money and quantity value objects | Implemented | `Money` enforces same-currency arithmetic; `Quantity` wraps decimal quantities |
| Canonical portfolio ledger types | Implemented | `PortfolioEntry`, `TradeEntry`, `CashEntry`, `PositionAdjustmentEntry`, `AssetTransferEntry`, and provenance types exist in the domain |
| FX lookup and conversion | Implemented for EUR target currency | `IFxRateLookup`, `FxConverter`, and `CsvFxRateLookup` exist |
| Historical price lookup and explicit instrument mapping | Implemented | `IHistoricalPriceLookup`, `IInstrumentMarketDataMap`, CSV OHLCV lookup, and JSON ISIN mapping exist |
| Lot realization | Implemented | `OpenLot`, `LotConsumption`, and `TradeMatchResult` support partial closes and remainder lots |
| FIFO long and short handling | Implemented | Matching supports closing longs with sells and closing shorts with buys |
| German tax ledger generation | Implemented with FX conversion to EUR | Sell, dividend, interest, withholding-tax, and `Vorabpauschale` entries exist and use the FX layer |
| Portfolio valuation snapshot | Implemented | Uses close prices, latest-on-or-before date handling, and EUR FX conversion |
| Instrument enrichment | Implemented | Imported instruments are enriched from local JSON profiles |

### Infrastructure And Delivery

| Capability | State | Notes |
|---|---|---|
| IBKR XML file import | Implemented | Reads one XML file or all XML files in a directory and maps to canonical ledger entries |
| Historical FX CSV generation | Implemented | A Python script downloads five years of ECB-based FX data and writes `fx_rates.csv` |
| Historical OHLCV CSV generation | Implemented | A Python script downloads five years of Yahoo Finance data using explicit ISIN mapping and writes `historical_prices.csv` |
| Diagnostics collection | Implemented | Invalid records, unsupported sources, ignored assets, and cancellation cleanup are reported |
| Local reference data | Implemented | Basis interest rates and year-end prices are read from CSV; profiles from JSON |
| Console reporting | Implemented | Yearly console report prints grouped tax sections and an estimated tax line |

## Test Coverage Snapshot

- Domain tests verify lot consumption invariants and pro-rata cost reduction.
- Application matcher tests verify FIFO ordering, partial closes, over-closes, long and short PnL behavior, and lot filtering by account/instrument.
- Application tax tests verify dividends, interest, withholding tax, `Vorabpauschale`, and deduction of prior `Vorabpauschale` on sale.
- A regression test validates the sample data again after introducing the dedicated FX lookup.

## Important Current Limitations

- The only implemented broker adapter is `Interactive Brokers` XML.
- The only delivery host is the CLI.
- The CLI depends on local configuration files such as `instruments.json`, `basiszins.csv`, and `prices.csv`.
- The CLI now also depends on `fx_rates.csv` for EUR conversion during tax replay.
- Portfolio valuation currently depends on `market_data_mappings.json` and `historical_prices.csv`.
- The CLI project references copied input content from `data/old_project/...`, which indicates a historical sample-data dependency in the current implementation.
- The earlier `AccountEvent` model has been removed from the active code path in favor of the canonical ledger.
- The repository does not yet contain authoritative product-roadmap or long-term architecture documents beyond the current-state slice.
