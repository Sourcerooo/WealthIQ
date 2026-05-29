# Tax Reference Data Design

This document describes the current reference-data adapters used by the tax pipeline.

## Instrument Profiles

- Adapter: `JsonInstrumentProfileEnricher`
- Input: local JSON file
- Purpose: enrich imported instruments with a stable display name and `Teilfreistellungsquote`

Current behavior:

- File must exist or the adapter throws.
- Profiles are keyed by ISIN.
- If a profile exists, the imported instrument is enriched from it.
- If no profile exists, the adapter preserves existing data and uses fallback values.

## Basis Interest Rates

- Adapter: `CsvBasisInterestRateProvider`
- Input: local CSV file
- Purpose: provide yearly basis interest rates for `Vorabpauschale`

Current behavior:

- File must exist or the adapter throws.
- Rows are parsed as `year,rate` after the header row.
- Missing years return `0m`.

## Year-End Prices

- Adapter: `CsvYearEndPriceProvider`
- Input: local CSV file
- Purpose: provide yearly ISIN-based year-end prices for `Vorabpauschale`

Current behavior:

- File must exist or the adapter throws.
- Rows are parsed as `year,isin,price` after the header row.
- Missing values return `null`.
