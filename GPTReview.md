# GPT Review

Review scope: current branch `feature/sigmaticConversion` compared to `main`.

Verification run: `dotnet test WealthIQ.slnx --no-restore` passes: 96/96 tests. The run emits two high-severity NuGet vulnerability warnings for `System.Security.Cryptography.Xml` 9.0.0.

## Summary

The implementation is substantial and generally well covered by focused tests. The largest concerns are not style issues; they are correctness gaps in fail-fast import behavior, idempotent persistence, and German tax replay completeness. Several issues can produce plausible but wrong tax reports without surfacing an error, which is the main risk for this project.

## Findings

### High: Duplicate source references inside one import batch are persisted

Files:

- `src/WealthIQ.Infrastructure/Persistence/SqliteLedgerStore.cs:16-28`
- `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs:20-24`

`SqliteLedgerStore.SaveLedgerAsync` checks for an existing `(SourceSystem, SourceRecordReference)` in the database before adding each ledger entry. It does not account for entries already added to the same `DbContext` during the current loop, and the EF index is not unique.

Impact: if the same source transaction appears twice in one imported ledger/batch, both rows can be inserted. That violates the repository rule that re-import/idempotency dedups over `SourceProvenance` transaction reference.

Suggested improvement: make `(SourceSystem, SourceRecordReference)` unique in the database and deduplicate within the incoming ledger before adding rows. Add tests for duplicate source references in the same `PortfolioLedger` and across repeated imports.

### High: Malformed required IBKR values silently become zero or year 0001 entries

File: `src/WealthIQ.Infrastructure/Ibkr/Import/IbkrStatementImporter.cs:160-168`, `454-475`

`ParseDecimal` returns `0m` on parse failure, and `ParseDateTimeOffset` returns `DateTimeOffset.MinValue` for missing or malformed dates. Callers then create canonical `TradeEntry` or `CashEntry` objects from those values.

Impact: bad `quantity`, `tradePrice`, `amount`, or date fields can become valid-looking ledger entries with zero values or `0001-01-01` dates. This conflicts with the fail-fast/no-silent-drops guidance and can corrupt tax replay.

Suggested improvement: parse required fields explicitly per record type. Emit `Error`/`Fatal` diagnostics for missing or invalid required fields and prevent persistence. Add importer/pipeline tests for malformed quantity, amount/price, and date fields.

### High: Missing year-end prices suppress Vorabpauschale instead of failing

File: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs:204-208`

When a long fund/ETF lot exists and the basis interest rate is positive, the calculator asks for the year-end price. If no price is available, it just continues.

Impact: required reference data can be missing while the tax report still succeeds with understated or missing Vorabpauschale. This conflicts with the project guardrail that missing required FX/reference/price data is blocking.

Suggested improvement: throw a clear exception or return a structured blocking result when the price is required but missing. Replace any test that expects skipping with one that expects a blocking failure.

### High: Vorabpauschale is skipped for years with no ledger activity

File: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs:33-52`

The calculator groups by years present in `portfolioLedger.Entries` and performs year-end closing only for those years.

Impact: if a fund is bought in 2023, held through all of 2024 with no entries, and sold in 2025, no 2024 year-end closing runs. The 2025-01-01 Vorabpauschale entry is missing, and the later sale does not deduct that previously taxed amount.

Suggested improvement: replay across the full relevant year range from first open lot/acquisition to final report year, not only years with ledger entries. Add a test for buy in N, quiet holding year N+1, sale in N+2.

### High: Dividend reduction for Vorabpauschale is applied too broadly

File: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs:137-147`, `210-223`

Dividend distributions are stored as one per-share value per `(year, instrument)` and then subtracted from every remaining lot of that instrument at year-end.

Impact: lots acquired after a dividend date receive a reduction for a distribution they did not receive. The calculation also ignores account boundaries, so a dividend in one account can reduce Vorabpauschale for lots in another account.

Suggested improvement: track distribution reductions by lot or at least by account/instrument and holding interval. Add tests for a dividend before a second buy and for the same instrument held in multiple accounts.

### Medium/High: Asset transfers and position adjustments are silently ignored by tax replay

File: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs:38-48`

The branch introduces `AssetTransferEntry` and `PositionAdjustmentEntry`, but tax replay only handles `TradeEntry` and `CashEntry`.

Impact: a sale after an internal asset transfer can be treated as a short/no-cost-basis situation in the receiving account, because matching is account-scoped and the transferred lot never moves. Unsupported canonical entry types should not be silently ignored in tax replay.

Suggested improvement: either implement transfer/adjustment semantics or fail fast with a clear unsupported-entry error. Add tests for an internal transfer followed by sale in the receiving account.

### Medium: Blazor Server uses a scoped EF DbContext for circuit-scoped services

File: `src/WealthIQ.Web/Program.cs:35-41`

`AddDbContext<WealthIqDbContext>` registers the context as scoped. In Blazor Server, scoped services are circuit-scoped, not request-scoped.

Impact: the same `DbContext` can be reused across components/events in one circuit. Concurrent UI actions or long-running import/report operations can trigger EF concurrency errors or leak tracked state across operations.

Suggested improvement: use `AddDbContextFactory` / `IDbContextFactory<WealthIqDbContext>` and create a fresh context per store operation, or otherwise ensure stores are operation-scoped. Add a composition/concurrency test for resolving and using stores independently.

### Medium: Web app data/reference paths are hard-coded to repo layout

Files:

- `src/WealthIQ.Web/Program.cs:21-27`, `69-75`
- `src/WealthIQ.Web/WealthIQ.Web.csproj:13-20`

The app derives `data/` from `ContentRootPath/../..` and seeds from repo-level reference files. There is no appsettings override, and the reference files are not included as Web content.

Impact: the app works in the local source-tree layout but is fragile for `dotnet publish`, service hosting, or any different content-root layout. Startup can fail before serving the UI if the seed files are not found.

Suggested improvement: move paths into configuration with sensible defaults, document them, and include required reference files in deployment or copy them as content. Add a startup/config test using temp configured paths.

### Medium: Reference-data CSV seeding silently drops malformed rows

File: `src/WealthIQ.Infrastructure/ReferenceData/ReferenceDataSeeder.cs:52-87`, `111-130`

CSV readers skip rows with too few columns or values that fail parsing. FX rows with invalid or non-positive rates are silently ignored.

Impact: committed seed data can be incomplete while startup succeeds, with failures surfacing later as missing FX/rate/price data. This is contrary to fail-fast behavior for required reference data.

Suggested improvement: make malformed rows throw with file path and line number. Add tests for invalid FX, basiszins, and price rows.

### Medium: FIFO tie-break can be non-deterministic for same-timestamp lots

Files:

- `src/WealthIQ.Application/Matcher/FiFoMatcher.cs:21-22`
- `src/WealthIQ.Domain/Model/Ledger/PortfolioLedger.cs:19-22`

The ledger orders same timestamps by `SourceRecordReference`, but `FiFoMatcher` sorts open lots only by `OpenOccurredAt`. `List<T>.Sort` is not stable, and `OpenLot` does not retain the source reference.

Impact: two same-timestamp buys with different prices/fees can be consumed in a non-broker order during a later partial sale, changing realized gains.

Suggested improvement: carry a deterministic tie-breaker into `OpenLot` or avoid re-sorting away ledger order. Add a test with same-timestamp buys, different source references/prices, and a partial sale.

### Medium: Failed import diagnostics are not persisted for audit

File: `src/WealthIQ.Application/Import/StatementImportPipeline.cs:43-49`

If importer diagnostics contain `Error` or `Fatal`, the pipeline returns an aborted result without persisting an import batch or diagnostics.

Impact: after refresh or process exit, there is no audit trail for failed imports, even though diagnostics are a core part of the import experience. This may be intentional transaction purity, but the desired behavior should be explicit.

Suggested improvement: decide and document whether aborted imports should leave no DB trace or persist a failed batch with diagnostics and no ledger entries. Add tests for the chosen behavior and audit visibility.

### Low: Production exception handler routes to missing `/Error` page

File: `src/WealthIQ.Web/Program.cs:78-80`

The production exception handler uses `/Error`, but no page/component for that route exists in the branch.

Impact: production errors may show a not-found/generic failure rather than a useful error page.

Suggested improvement: add an `/Error` page or change the handler target to an existing route. Add a minimal routing/startup test if Web tests are introduced.

### Low: Import page temp files can leak when file selection changes

File: `src/WealthIQ.Web/Components/Pages/Import.razor:83-99`

`OnFilesSelected` clears `_pendingFiles` before deleting previous temp files. If the user selects files and then selects another set before importing, the previous temp copies remain in `%TEMP%`.

Suggested improvement: delete any existing pending temp files before clearing/replacing the list.

### Low: Import progress text always shows `0/0` while processing

File: `src/WealthIQ.Web/Components/Pages/Import.razor:30-34`, `123-124`, `152-153`

`RunImport` copies `_pendingFiles` to `toProcess`, then clears `_pendingFiles`. The button label uses `_pendingFiles.Count`, so during processing it displays `Importiere... (0/0)` instead of the actual total.

Suggested improvement: keep a separate `_totalCount` for the current run.

### Low: Audit `isin` query parameter is only applied on first initialization

File: `src/WealthIQ.Web/Components/Pages/Audit.razor:72-74`, `88-93`

`IsinQuery` is copied into `_filterIsin` only in `OnInitializedAsync`. If Blazor reuses the component while navigating from `/audit` to `/audit?isin=...`, the filter can remain stale.

Suggested improvement: apply query parameter changes in `OnParametersSet` / `OnParametersSetAsync`.

## Additional Improvement Suggestions

- Address the `System.Security.Cryptography.Xml` 9.0.0 vulnerability warnings, either by upgrading the package or removing the dependency if it is transitive and no longer needed.
- Add Web-level tests or at least a DI composition test. Current tests cover Domain/Application/Infrastructure well, but they do not validate Web startup, service resolution, error routing, or Blazor page behavior.
- Consider making tax calculation return a structured result that can carry blocking diagnostics instead of throwing for every missing-reference condition. The important part is that missing required tax/reference data must not silently produce a report.
