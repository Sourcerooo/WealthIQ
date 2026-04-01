# German Tax Calculation Design

This document describes the currently implemented behavior of `GermanTaxCalculator`.

## Inputs

- chronological `PortfolioLedger`
- enriched instrument catalog
- basis-interest-rate provider
- year-end-price provider

## High-Level Flow

```text
sort portfolio entries by time
  -> group by calendar year
  -> process trade and cash events for the year
  -> perform year-end closing for remaining long lots
  -> emit `GermanTaxEntry` records and updated open lots
```

## Event Processing Implemented Today

### Trades

- Buy events open long lots directly when no matching short lots exist.
- Otherwise trades are routed through `FiFoMatcher`.
- For each realized consumption, the calculator emits a `GermanTaxEntry` of type `Sell`.
- Previously accumulated `Vorabpauschale` consumed from the lot is deducted from the raw sell profit.
- `Teilfreistellungsquote` is applied to derive taxable sell profit.

### Cash Income

- Dividends create `GermanTaxEntry` values of type `Dividend`.
- Dividend taxable amount is reduced by `Teilfreistellungsquote`.
- Dividend processing also tracks per-share distributions for later year-end `Vorabpauschale` calculation.
- Interest creates `GermanTaxEntry` values of type `Interest` without `Teilfreistellungsquote` reduction.

### Withholding Tax

- Withholding tax creates `GermanTaxEntry` values of type `WithholdingTax`.
- The foreign withholding amount is stored as a positive `ForeignWithholdingTax` value.

## Year-End Closing

For each year and each open long lot group with a known ISIN and available year-end price:

- load basis interest rate for the year
- compute the basis factor as `basisInterestRate * 0.7`
- compute acquisition-price-based basis yield
- compute appreciation against the year-end price
- compute the capped per-share `Vorabpauschale`
- reduce it by tracked per-share distributions for that year
- store total `Vorabpauschale` on the lot as `AccumulatedVorabpauschale`
- emit a `GermanTaxEntry` of type `Vorabpauschale` dated `January 1` of the following year

## Output

- `GermanTaxCalculationResult.Entries`
- `GermanTaxCalculationResult.OpenLots`

## Current Scope Notes

- The current calculator processes the ledger entry types it knows about; unsupported source rows must already be filtered at import time.
- The implementation is intentionally driven by local reference data rather than external services.
- Multi-currency conversion is not implemented yet, so trustworthy tax replay still requires a future FX conversion layer.
