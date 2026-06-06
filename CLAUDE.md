# CLAUDE.md
Guidance for Claude Code and other agentic assistants working in `WealthIQ`.

## What this project is
WealthIQ is a **personal, single-user, local** wealth-management tool. v1 priority is a **German annual tax report** (Finanzamt-grade basis): import an IBKR XML statement → canonical ledger in SQLite → yearly tax report in a local Blazor dashboard with drill-down to the source.

**Explicitly out of v1 scope** (do not add unless asked): portfolio valuation/charts, PDF export, additional brokers, trading strategies/backtesting, multi-base-currency. The retired "Sigmatic" project was the reference implementation; its tax logic was ported, its trading engine was not.

Authoritative docs in repo:
- Spec: `docs/superpowers/specs/2026-05-29-wealthiq-neustart-design.md`
- Plans: `docs/superpowers/plans/` (Plan 1 foundation/persistence, Plan 2 import→persist + seeding, Plan 3 tax-replay + dashboard) — **all implemented**.
- Phase 2 spec: `docs/superpowers/specs/2026-05-31-phase2-data-administration-design.md`
- Phase 2 plan: `docs/superpowers/plans/2026-06-03-phase2-data-administration.md`
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

### Web UI ("Midnight Ledger" design)
The dashboard uses a dark-first emerald-on-navy theme with a left navigation drawer and a dark/light toggle. Key pieces (all under `src/WealthIQ.Web`):
- Theme: central `MudTheme` in `Theme/WealthIqTheme.cs` (light + dark palettes), wired in `Components/Layout/MainLayout.razor` via `MudThemeProvider`.
- Theme persistence: `Services/ThemePreferenceService.cs` stores the dark/light choice in `localStorage` (JS interop, loaded in `OnAfterRenderAsync(firstRender)`).
- Shared presentational components in `Components/Shared/`: `PageHeader` (renders the page `<h1>` — keep it so `FocusOnNavigate Selector="h1"` works), `SectionCard`, `StatCard`.
- Styling/motion: `wwwroot/wealthiq.css` (`.wiq-*` classes, tabular numerals, entrance animations, honors `prefers-reduced-motion`) and `wwwroot/wealthiq.js` (`window.wealthiq` — theme localStorage helpers + count-up enhancement; the static text is already the final value, JS only animates).
- Navigation drawer groups pages as **Bericht** (Steuerreport), **Daten erfassen** (Import), **Daten ansehen** (Data Browser: Ledger = `/browse/ledger`, Kurschart = `/browse/prices`, Wechselkurse = `/browse/fx`), **Stammdaten** (Marktdaten = `/data-admin`, Instrumente), and a bottom-pinned **Diagnose** (Diagnose = `/diagnostics`, Audit-Trail).
- **Data Browser** (`Components/Pages/Browse/`) is read-only and queries `WealthIqDbContext`/`ILedgerStore` directly (same precedent as `DataAdmin`/`Audit`): `LedgerBrowser` (ledger split by entry kind, original currency), `PriceChart` (per-symbol adjusted candlestick), `FxChart` (per-currency `X/EUR` line). `LedgerBrowser` has a top **account dropdown** (lists only accounts with entries, preselects the first; switching re-filters the already-loaded entries in memory) and hosts the **ledger delete** (button + "Rohdateien löschen" checkbox via `ILedgerClearService`) — `DataAdmin` no longer does.
- `PriceChart` preselects the first instrument and **remembers the selection across navigation** via the scoped `Services/ChartSelectionState` (per-circuit, resets on full reload); its `MudAutocomplete` is `Clearable="false"`.
- Charts: the Steuerreport donut still uses MudBlazor's built-in `MudChart` (one `ChartSeries<double>` whose `Data` holds per-category segment values, rounded to 2 decimals). The Data Browser candlestick/line charts use **TradingView Lightweight Charts v4** — vendored at `wwwroot/lib/lightweight-charts/`, interop in `wwwroot/wiq-charts.js` (global `window.wiqCharts`), wrapped by the reusable `Components/Shared/LightweightChart.razor` (`Kind="candlestick"|"line"`, `IAsyncDisposable`, dark/light theme sync, optional `InitialRangeDays` to open zoomed to the last N days — Kurschart uses `365`). Adjusted OHLC is derived by `AdjustedPriceCalculator.ToAdjusted` (factor `AdjustedClose/Close`, display only — the tax engine still uses raw `Close`).
- **Marktdaten** (`DataAdmin`) hosts Historische Kurse, Wechselkurse, and Basiszins only (no Ledger/Instrumente panels — Instrumente has its own nav entry). Wechselkurse supports **incremental refresh** (`FxRateRefreshService.RefreshIncrementalAsync`, tracked set = stored currencies ∪ USD/GBP/CHF) and **add-currency backfill** (`AddCurrencyAsync` over the static ECB currency list; `IFxRateProvider.FetchAsync` takes a currency filter). Basiszins is a single **ascending** editable year→rate table (`MudTable` `RowEditCommit` + per-row delete + inline add-row via `BasisInterestRateRefreshService.SetManualAsync`/`DeleteAsync`); the BMF auto-fetch button was removed from the UI.
- `GermanTaxEntry` carries display-only fields (`OpenedOn`/`Fees`/`Origin`, plus `SourceReference`/`CloseReference`/`SourceFile`/`OriginalAmount`/`OriginalCurrency` and the Vorabpauschale inputs `YearStartPrice`/`YearEndPrice`/`BasisRate`/`HeldQuantity`/`DistributionPerShare`/`MonthFactor`) populated by `GermanTaxCalculator` (additive, never affect tax math). The Steuerreport tables show these via **in-place expander rows** ("Quelle"/"Weniger") instead of navigating away; the Verkäufe summary "Anzeigen" scroll-and-highlights its detail row (`wealthiq.scrollAndHighlight`). The "Verrechn. Vorabpausch." column shows only on the Verkäufe summary (hidden for Dividenden/Zinsen/Vorabpauschale where it is always 0).
- Note: `Components/Shared/` Razor components have no `Class`/`Style` parameter; apply outer spacing via a wrapper `div`. Icon-only `MudIconButton`s use `aria-label` (not `Title`, which trips the MUD0002 analyzer).

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
- `data/reference/` — **committed** seed reference data (`basiszins.csv`, `instruments.json`, `fx_rates.csv`, `listings.json`, `historical_prices.csv`). Seeded into SQLite on first run. `prices.csv` and `market_data_mappings.json` are **retired**; year-start/year-end prices are derived from `HistoricalPrice` bars (`ProviderSymbol, Date`). New tables: `HistoricalPrice`, `InstrumentListing` (`Isin, Currency`), `DataRefreshLog` (`Dataset`). `InstrumentProfile` now carries `Type` and `SubjectToVorabpauschale` columns. Reference data is refreshable from the internet (Yahoo Finance / ECB / BMF) via the `/data-admin` page; committed files remain the offline bootstrap seed and CI fixtures. Python download scripts are **retired**; native C# providers replace them.
- `data/test/` — **committed** golden test fixtures: `statements/*.xml` (IBKR samples 2021–2025) + `configuration/` (csv/json the regression test reads). **Must not be gitignored** — the end-to-end regression test reads them, so gitignoring them breaks CI.
- `data/app/` — local runtime DB/raw files. **Gitignored.**
- `data/design_template/` — local-only design notes. **Gitignored.**

## Tax-pipeline guardrails
- Lot matching is **FIFO**; distinguish open lots from realized entries; partial closes preserve remaining quantity + pro-rata cost allocation; over-closes open an opposite-direction lot (short), never silently dropped.
- **Vorabpauschale** (§18 InvStG) — only for instruments where `SubjectToVorabpauschale = true` (explicit profile required; no inference). Per year Y with `Basiszins(Y) > 0`, for each held long lot: `basisErtrag = yearStartRedemptionPrice × Basiszins × 0.7`; `cap = max(0, (yearEnd − yearStart) + distributionsPerShare)`; `vorabFull = max(0, min(basisErtrag, cap) − distributionsPerShare)`; `vorabPerShare = vorabFull × monthFactor` where `monthFactor = (13 − openMonth)/12` in the acquisition year, else 1. Posted to year+1 (Jan 1); previously-taxed Vorabpauschale deducted at sale. `Basiszins = null` for a held year → blocking error. `Basiszins ≤ 0` → skip year (no price lookup).
- **Teilfreistellung** (e.g. 30% for equity funds) applies to sales, dividends, and Vorabpauschale; driven by the instrument profile. No defaults — a held instrument with no profile is a blocking error.
- Importer accepts only `STK`/`FUND`; forex/cash/other asset classes → Info diagnostic + skip. Cancellation pairs ("(Ca.)") are matched and removed.
- Golden baseline: `tests/.../Application/Tax/GermanTaxRegressionTests.cs` asserts exact 2024 disposal + Vorabpauschale figures against `data/test`. If tax logic changes, update expected values deliberately and explain why.
- Known thin spots: short-position tax semantics; Teilfreistellung variants other than 30%/0% (no such instruments in data yet); Vorabpauschale for a position held *beyond* the last ledger entry still needs an explicit as-of/through-year parameter (calculator currently replays only up to the last entry year). Multi-year Vorabpauschale accumulation and the §18(2) acquisition-month pro-ration are fully implemented and tested as of Phase 2.
- **No loss carry-forward / Verlustverrechnungstöpfe (estimate-only limitation).** Per §19 InvStG the previously-taxed Vorabpauschale is deducted at sale (`GermanTaxCalculator` line ~104, `rawProfit = proceeds − cost − usedVorab`), so a sale at a loss correctly grows the loss — covered by `GermanTaxCalculatorTests.Calculate_SellAtLossAfterVorabpauschale_DeductionEnlargesTheLoss`. But `AnnualTaxReportService` aggregates **strictly per year** and clamps each year's tax at `Math.Max(0, taxableBase) × 26.375%` — a net loss in year Y is **not** carried into other years, and there is **no separation of loss pots** (Aktien- vs. allgemeiner Verrechnungstopf; all sells/dividends/interest/Vorab share one base). Real losses are preserved by the Finanzamt as a Verlustvortrag, so WealthIQ's per-year `EstimatedTax` can **overstate** tax in years following a loss. Full Verlustverrechnungstöpfe + carry-forward (requires per-instrument bucket classification) is deferred future work.
- `AssetTransferEntry` / `PositionAdjustmentEntry` exist in the domain but tax replay fails fast with `NotSupportedException` if encountered — full transfer/adjustment semantics are unimplemented (no importer constructs them yet; YAGNI until IBKR data requires it).

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
