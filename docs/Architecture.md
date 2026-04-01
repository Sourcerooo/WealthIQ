# Architecture -- WealthIQ

This document is the normative high-level architecture overview for WealthIQ.
It describes the currently implemented product slice, tech stack, component boundaries, and dependency rules.
It does not define detailed algorithms line by line.

For detailed design and implementation guidance, continue in `docs/architecture/index.md`.

## Current Product Slice

WealthIQ currently implements a local processing pipeline for broker statement imports and German tax reporting.

Current implemented flow:

```text
IBKR XML files + local tax reference files
  -> broker-specific import
  -> canonical account events + instrument catalog
  -> FIFO lot matching + yearly tax calculation
  -> console tax report
```

The implemented system currently covers:

- Interactive Brokers XML import
- canonical account event normalization for trades, dividends, interest, and withholding tax
- FIFO-based lot realization across long and short positions
- German tax ledger generation including sell entries, dividends, interest, withholding tax, and `Vorabpauschale`
- console reporting for yearly tax output

## Documentation Structure

The architecture documentation is split by responsibility and level of detail.

| Document area | Purpose | Normative |
|---|---|---|
| `docs/Architecture.md` | High-level architecture, tech stack, components, dependency rules | Yes |
| `docs/architecture/index.md` | Navigation, reading order, and document map | Yes |
| `docs/architecture/components/*.md` | Component responsibilities and boundaries | Yes |
| `docs/architecture/design/*.md` | Technical design, contracts, state ownership, and data flows | Yes |
| `docs/architecture/examples/*.md` | Example flows and illustrative walkthroughs | No |
| `docs/architecture/glossary.md` | Shared architectural vocabulary | Yes |
| `docs/architecture/open-questions.md` | Open architecture and design questions | Informative |
| `docs/architecture/current-state.md` | Current implementation maturity snapshot | Informative |

## Tech Stack

- Language and runtime: C# on .NET `net10.0`
- Architecture style: layered architecture with strict inward dependency direction
- Core business model: immutable records/value objects around account events, lots, and tax entries
- Delivery host: console application
- Import technology: `System.Xml.Linq` over local IBKR XML files
- Tax reference data: local JSON and CSV files
- Testing: xUnit test project with domain, application, and regression-style coverage

## High-Level Architecture

```text
+---------------------------------------------------------------------------+
|                              DELIVERY LAYER                               |
|                                                                           |
|  CLI host              Console report writer                              |
+---------------------------------------------------------------------------+
|                           INFRASTRUCTURE LAYER                            |
|                                                                           |
|  IBKR XML importer     JSON instrument profiles   CSV rates and prices    |
+---------------------------------------------------------------------------+
|                          APPLICATION LAYER                                |
|                                                                           |
|  Import contracts      FIFO matcher             German tax calculation     |
|  Instrument enrichment Tax reference ports                              |
+---------------------------------------------------------------------------+
|                             DOMAIN LAYER                                  |
|                                                                           |
|  Account events        Open lots and consumptions  Tax entries            |
|  Money/quantity        Accounts and instruments    Business vocabulary     |
+---------------------------------------------------------------------------+
```

High-level interaction model:

```text
CLI
  -> Infrastructure importer and reference-data adapters
  -> Application services
  -> Domain model
  -> Console output
```

## Core Architecture Rules

- Preserve dependency direction: `Domain <- Application <- Infrastructure <- Delivery`.
- `Domain` owns core business concepts, invariants, and calculations derived from value objects.
- `Application` owns use-case-level orchestration such as lot matching, tax calculation, and import-facing contracts.
- `Infrastructure.IBKR` owns broker-specific parsing, file handling, and local tax reference-data adapters.
- `Cli` stays thin and delegates import, catalog building, tax calculation, and report formatting.
- Broker-specific details must not leak into `Domain`.
- Current calculations assume EUR-normalized monetary values at the import boundary.

## Component Overview

- `WealthIQ.Domain`: core account-event model, lot model, tax result model, value objects, and enums
- `WealthIQ.Application`: import contracts, FIFO matcher, German tax calculator, tax reference-data ports, instrument catalog enrichment
- `WealthIQ.Infrastructure.IBKR`: IBKR XML import adapter and file-based tax reference-data adapters
- `WealthIQ.Cli`: current executable workflow for import, tax calculation, diagnostics, and console reporting
- `WealthIQ.Tests`: domain, application, and end-to-end regression tests against sample input

Detailed responsibilities live in:

- `docs/architecture/components/domain.md`
- `docs/architecture/components/application.md`
- `docs/architecture/components/infrastructure.md`
- `docs/architecture/components/delivery.md`
- `docs/architecture/components/quality-and-operations.md`

## Dependency Rules

Current project references:

```text
WealthIQ.Application        -> WealthIQ.Domain
WealthIQ.Infrastructure.IBKR-> WealthIQ.Application, WealthIQ.Domain
WealthIQ.Cli                -> WealthIQ.Application, WealthIQ.Domain, WealthIQ.Infrastructure.IBKR
WealthIQ.Tests              -> WealthIQ.Application, WealthIQ.Domain, WealthIQ.Infrastructure.IBKR
```

Rules to preserve:

- `WealthIQ.Domain` must stay independent from application, infrastructure, and delivery code.
- `WealthIQ.Application` may depend on `WealthIQ.Domain` only.
- `WealthIQ.Infrastructure.IBKR` should implement application-facing contracts instead of introducing broker-specific behavior into inner layers.
- `WealthIQ.Cli` should remain an orchestration and presentation host.
- The current test project references infrastructure directly for regression scenarios; this is part of the current implementation state, not a rule for all future tests.

## Repository Structure

Current repository structure relevant to the implemented product slice:

```text
.
|-- AGENTS.md
|-- WealthIQ.slnx
|-- src/
|   |-- WealthIQ.Domain/
|   |-- WealthIQ.Application/
|   |-- WealthIQ.Infrastructure.IBKR/
|   `-- WealthIQ.Cli/
|-- tests/
|   `-- WealthIQ.Tests/
|-- docs/
|   |-- Architecture.md
|   `-- architecture/
`-- data/
    |-- design_template/
    `-- old_project/
```

## Related Documents

- Start at `docs/architecture/index.md` for the reading guide.
- Use `docs/architecture/glossary.md` for canonical terms.
- Use `docs/architecture/current-state.md` for maturity tracking.
- Use `docs/architecture/open-questions.md` for unresolved repository-level questions.
