# End-To-End Tax Report Flow

This example illustrates the current implemented path from source files to console output.

```text
XML files in input directory
  -> `IbkrStatementImporter`
  -> `ImportResult`
     - `PortfolioLedger`
     - `Instruments`
     - `Diagnostics`
  -> `InstrumentCatalogBuilder`
  -> `GermanTaxCalculator`
  -> `GermanTaxCalculationResult`
  -> `TaxReportConsoleWriter`
```

Example sequence:

1. The CLI resolves an input path.
2. The importer reads XML files and maps rows into canonical portfolio entries.
3. Diagnostics are collected for ignored or invalid records.
4. Instruments are enriched from local JSON profiles.
5. The calculator processes the canonical ledger year by year.
6. The console writer prints grouped yearly tax sections.

This file is illustrative only.
