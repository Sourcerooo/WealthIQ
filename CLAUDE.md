# CLAUDE.md
Guidance for Claude Code and other agentic assistants working in `WealthIQ`.

## What this project is
WealthIQ is a **personal, single-user, local** wealth-management tool. v1 priority is a **German annual tax report** (Finanzamt-grade basis): import an IBKR XML statement → canonical ledger in SQLite → yearly tax report in a local Blazor dashboard with drill-down to the source.

**Explicitly out of v1 scope** (do not add unless asked): portfolio valuation/charts, PDF export, additional brokers, trading strategies/backtesting, multi-base-currency. The retired "Sigmatic" project was the reference implementation; its tax logic was ported, its trading engine was not.

Authoritative docs in repo:
- Spec: `docs/superpowers/specs/2026-05-29-wealthiq-neustart-design.md`
- Plans: `docs/superpowers/plans/` (Plan 1 foundation/persistence, Plan 2 import→persist + seeding, Plan 3 tax-replay + dashboard) — **all implemented**.
- Old docs (discussion basis, do not delete): `docs_old/`

## Stack
- C# / .NET 10 (`net10.0`), SDK-style projects, solution `WealthIQ.slnx`
- Nullable reference types **enabled**; implicit usings enabled
- Blazor Server + MudBlazor (local web dashboard); EF Core + **SQLite**
- Tests: xUnit

## Repository layout
- `src/WealthIQ.Domain` — pure core: value objects, canonical ledger, lots, tax result types. No IO/EF.
- `src/WealthIQ.Application` — use-cases & ports (interfaces): import pipeline, FIFO matcher, `GermanTaxCalculator`, FX conversion, replay/report services.
- `src/WealthIQ.Infrastructure` — port implementations: EF Core + SQLite persistence (`Persistence/`), IBKR importer + CSV/JSON reference adapters (`Ibkr/`), reference-data seeder.
- `src/WealthIQ.Web` — Blazor Server app; **composition root** (DI wiring) and the only project that references Infrastructure.
- `tests/WealthIQ.Tests` — xUnit (Domain / Application / Infrastructure).

## Dependency direction (keep strict)
- `Application → Domain`
- `Infrastructure → Application, Domain`
- `Web → Application, Domain, Infrastructure` (composition only)
- `Tests → Application, Domain, Infrastructure`

Rules: never depend from `Domain` outward; business rules live in `Domain`/`Application`; broker/parsing/persistence concerns live in `Infrastructure`; **only `Web` references `Infrastructure`** so persistence does not leak into Application/Domain.

## Architecture principles (inviolable)
- **Canonical ledger is the source of truth.** Positions, realizations, and tax are reconstructed by **replay** from the ordered, immutable `PortfolioEntry` set — never the reverse.
- Ledger entries store amounts in **original currency**. Never embed an EUR conversion in an entry.
- **FX rule:** convert to EUR only at replay/accumulation, using the FX rate **at the event's own time** (trade/booking/acquisition date), not at accumulation time. A **missing required rate is a blocking error** — no silent fallback. (`CsvFxRateLookup` / `DbFxRateLookup` support `ExactDate` and `NextAvailableOnOrAfter` roll-forward for statutory dates.)
- **Re-import is idempotent** — dedup over `SourceProvenance` transaction reference.
- **Fail-fast everywhere.** Each source record becomes a canonical entry or a structured `ImportDiagnostic` (Severity Info/Warning/Error/Fatal). Collect *all* diagnostics, then abort the batch transactionally if any are blocking. No silent drops; missing reference/FX/price data fails loudly.
- `PortfolioLedger` ordering is deterministic: same `OccurredAt` ties break on `SourceProvenance.SourceRecordReference` (broker booking order = true FIFO). Do not reintroduce GUID-based tie-breaks.

## Build / test commands (run from repo root)
- Restore: `dotnet restore WealthIQ.slnx`
- Build: `dotnet build WealthIQ.slnx`
- Clean rebuild (stale artifacts / file locks): `dotnet clean WealthIQ.slnx && dotnet build WealthIQ.slnx`
- All tests: `dotnet test WealthIQ.slnx`
- Single test: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxRegressionTests"`
- By display-name substring: `dotnet test WealthIQ.slnx --filter "DisplayName~Vorabpauschale"`
- Format check / fix: `dotnet format WealthIQ.slnx --verify-no-changes` / `dotnet format WealthIQ.slnx`

## CI (must stay green on PRs)
- `.github/workflows/ci-tests.yaml` runs on pull requests: restore → **build Release** → `dotnet test WealthIQ.slnx --configuration Release --no-build`.
- `--no-build` means the Release build must succeed first, and **everything the tests read must be committed to git** (CI clones a clean checkout).

## Data layout (`data/`)
- `data/reference/` — **committed** seed reference data (`basiszins.csv`, `prices.csv`, `instruments.json`, `fx_rates.csv`, plus market-data tooling inputs `market_data_mappings.json`, `historical_prices.csv`). Seeded into SQLite on first run.
- `data/test/` — **committed** golden test fixtures: `statements/*.xml` (IBKR samples 2021–2025) + `configuration/` (csv/json the regression test reads). **Must not be gitignored** — the end-to-end regression test reads them, so gitignoring them breaks CI.
- `data/app/` — local runtime DB/raw files. **Gitignored.**
- `data/design_template/` — local-only design notes. **Gitignored.**

## Tax-pipeline guardrails
- Lot matching is **FIFO**; distinguish open lots from realized entries; partial closes preserve remaining quantity + pro-rata cost allocation; over-closes open an opposite-direction lot (short), never silently dropped.
- **Vorabpauschale** = `Basiszins × 0.7` pro-rata months, capped at actual appreciation, minus same-year distributions; posted to year+1; previously-taxed Vorabpauschale is deducted at sale.
- **Teilfreistellung** (e.g. 30% for equity funds) applies to sales, dividends, and Vorabpauschale; driven by instrument profile (default 30% when ISIN present but unknown).
- Importer accepts only `STK`/`FUND`; forex/cash/other asset classes → Info diagnostic + skip. Cancellation pairs ("(Ca.)") are matched and removed.
- Golden baseline: `tests/.../Application/Tax/GermanTaxRegressionTests.cs` asserts exact 2024 disposal + Vorabpauschale figures against `data/test`. If tax logic changes, update expected values deliberately and explain why.
- Known thin spots: short-position tax semantics; Teilfreistellung variants other than 30%/0% (no such instruments in data yet).

## EF Core / migrations
- DbContext `WealthIqDbContext`; design-time factory `WealthIqDbContextFactory` (so no `--startup-project` needed). Migrations live in `src/WealthIQ.Infrastructure/Persistence/Migrations/`.
- Add a migration: `dotnet ef migrations add <Name> --project src/WealthIQ.Infrastructure`
- Apply: `dotnet ef database update --project src/WealthIQ.Infrastructure` (Web also migrates + seeds on startup).

## Coding conventions
- English for identifiers, docs, comments, commit messages.
- `PascalCase` types/methods/properties/enums; `camelCase` locals/parameters. Clear domain names (`Trade`, `Lot`, `Realization`, `Account`).
- One primary public type per file; file name matches the type; namespace mirrors folder path. Avoid huge files / deep nesting.
- Prefer implicit usings; remove unused; `System.*` before project usings; avoid ad-hoc global usings.
- `dotnet format`; 4 spaces, no tabs.
- `record` / `record struct` for immutable value-centric models; `readonly record struct` for tiny value objects.
- **`decimal` for money/quantities, never `double`.** Keep currency operations explicit and safe; use `DateOnly` vs `DateTimeOffset` intentionally.
- Treat nullable warnings as real; validate external input at boundaries (XML, UI); avoid `!` null-forgiving; prefer guard clauses.
- Throw specific exceptions for invariant violations with actionable messages; don't swallow exceptions; prefer structured results for recoverable business outcomes.
- `IReadOnlyList<T>` across boundaries; don't mutate caller collections; return updated values over mutating shared state.

## Testing conventions
- Add/update tests for every behavioral change. Names: `Method_Scenario_ExpectedResult`.
- Cover happy path, edge cases, invalid inputs. Keep tests deterministic — **no live network, no real-time, fixed reference data.**
- Prefer narrow, focused tests. Run targeted tests for touched areas; run the full suite before handoff.

## Agent workflow
- Inspect nearby files before editing; match local style/naming; keep changes scoped (no drive-by refactors).
- Port-and-refine the existing Domain/Application logic; don't rewrite correct tax/ledger code.
- Update this file and the spec when structure, contracts, commands, or tooling change.
- Git: commit/push only when asked; branch off `main` for feature work.

## Cursor / Copilot rules
Checked `.cursor/rules/`, `.cursorrules`, `.github/copilot-instructions.md` — none present. If added later, treat as higher-priority repo policy.
