# CLI Tax Reporting Design

This document describes the currently implemented CLI workflow.

## Entry Point

- `Program.Main(string[] args)`

## Runtime Flow

```text
resolve input path
  -> run IBKR import
  -> print warning-or-higher diagnostics
  -> stop on fatal diagnostics
  -> enrich instruments from `Configuration/instruments.json`
  -> load `basiszins.csv` and `prices.csv`
  -> calculate German tax entries
  -> print grouped yearly report sections
```

## Input Resolution

- If `args[0]` exists and is non-empty, it is used as the input path.
- Otherwise the CLI uses `AppContext.BaseDirectory/Input`.

## Output Sections

`TaxReportConsoleWriter` currently prints:

- ignored-assets summary
- sell section
- `Vorabpauschale` section
- dividend section
- interest section
- withholding-tax section
- yearly totals and estimated tax calculation

## Error Handling

- Fatal import diagnostics return exit code `1`.
- Unhandled exceptions are caught, printed as `ERROR: ...`, and also return exit code `1`.
- Successful execution returns exit code `0`.

## Current Scope Notes

- The CLI does not currently expose separate subcommands.
- The workflow is tax-report oriented rather than a general-purpose shell.
