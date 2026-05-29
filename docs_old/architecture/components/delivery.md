# Delivery Component

## Responsibility

`WealthIQ.Cli` is the current delivery host for the implemented WealthIQ slice.

It currently owns:

- resolving the input path
- invoking the importer
- printing diagnostics that matter for the run
- building the instrument catalog
- running the German tax calculation
- rendering yearly console reports

## What The Delivery Layer Must Not Own

- broker-specific parsing logic
- lot-matching rules
- tax calculation rules
- reference-data parsing details

## Current Shape

- There is one executable workflow in `Program.cs`.
- There is no command parser or multi-command shell yet.
- `TaxReportConsoleWriter` contains console formatting only.

## Boundary Notes

- The CLI currently creates infrastructure and application objects directly instead of using dependency injection.
- Runtime success depends on local sample/configuration files being present at the resolved input path.
