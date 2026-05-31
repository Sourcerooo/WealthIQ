# Code Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve the valid findings from the GPT 5.5 code review (`GPTReview.md`) — correctness gaps in fail-fast import, idempotent persistence, and German tax replay completeness, plus determinism, Blazor lifetime, and UI fixes.

**Architecture:** Changes are scoped to the existing layered architecture (Domain → Application → Infrastructure → Web). Tax-replay fixes stay in `WealthIQ.Application`; import/persistence fixes in `WealthIQ.Infrastructure`; lifetime/UI fixes in `WealthIQ.Web`. Two EF migrations are added (unique source-reference index; import-batch status column). TDD throughout: every behavioral change gets a failing test first.

**Tech Stack:** C# / .NET 10, EF Core + SQLite, xUnit, Blazor Server + MudBlazor.

**Branch:** `fix/codeReview` (already created from `main`).

---

## Validity triage (reference)

| Review finding | Verdict | Task |
|---|---|---|
| #2 Malformed IBKR values → 0 / year 0001 | Valid (fail-fast guardrail) | Task 1 |
| #1 Duplicate source refs persist within one batch | Valid (idempotency guardrail) | Task 2 |
| #3 Missing year-end price suppresses Vorabpauschale | Valid (blocking-data guardrail) — **overrides an existing test that asserted skip** | Task 3 |
| #4 Vorabpauschale skipped in quiet years | Valid | Task 4 |
| #5 Dividend reduction applied too broadly | Valid | Task 5 |
| #6 Transfer/adjustment entries silently ignored | Partly valid — **fail-fast guard only** (no entry constructs them; full semantics = YAGNI) | Task 6 |
| #10 FIFO tie-break non-deterministic | Valid (determinism guardrail) | Task 7 |
| #9 Seed CSV silently drops malformed rows | Valid (fail-fast guardrail) | Task 8 |
| #11 Failed-import diagnostics not persisted | Decision: **persist failed batch + diagnostics** | Task 9 |
| #7 Scoped DbContext in Blazor | Valid — **adopt IDbContextFactory** | Task 10 |
| #12 `/Error` page missing | Valid | Task 11 |
| #8 Hard-coded data paths | Valid — **light config fallback only** (full deployment hardening out of v1 scope) | Task 12 |
| #13 Temp files leak on re-selection | Valid | Task 13 |
| #14 Progress shows `0/0` | Valid | Task 14 |
| #15 Audit `isin` param stale on reuse | Valid | Task 15 |
| Crypto vuln (`System.Security.Cryptography.Xml`) | Valid (transitive) — pin | Task 16 |

**Deferred (out of v1 scope / YAGNI):** full transfer/adjustment tax semantics, full Web test suite + DI smoke test, structured tax-result refactor. Documented as known thin spots; not in this plan.

---

## File Structure

**Modified — Application (tax/import logic):**
- `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs` — quiet-year closing, blocking year-end price, dividend allocation, unsupported-entry guard (Tasks 3–6)
- `src/WealthIQ.Application/Matcher/FiFoMatcher.cs` — deterministic ordered match (Task 7)
- `src/WealthIQ.Application/Import/StatementImportPipeline.cs` — persist failed batch (Task 9)
- `src/WealthIQ.Application/Persistence/Interface/IImportStore.cs` — add `PersistFailedImportAsync` (Task 9)
- `src/WealthIQ.Application/Import/ImportBatch.cs` — add `Status` (Task 9)
- `src/WealthIQ.Application/Audit/ImportBatchView.cs` — add `Status` (Task 9)

**Modified — Domain:**
- `src/WealthIQ.Domain/Model/Lot/OpenLot.cs` — add `OpenSourceReference` tie-break (Task 7)

**Modified — Infrastructure:**
- `src/WealthIQ.Infrastructure/Ibkr/Import/IbkrStatementImporter.cs` — fail-fast required-field parsing (Task 1)
- `src/WealthIQ.Infrastructure/Persistence/SqliteLedgerStore.cs` — in-batch dedup (Task 2)
- `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs` — unique index (Task 2)
- `src/WealthIQ.Infrastructure/Persistence/SqliteImportStore.cs` — failed-batch persistence + status (Task 9)
- `src/WealthIQ.Infrastructure/Persistence/SqliteImportAuditStore.cs` — surface status (Task 9)
- `src/WealthIQ.Infrastructure/Persistence/Rows/ImportBatchRow.cs` + `Mapping/ImportBatchMapper.cs` — status column (Task 9)
- `src/WealthIQ.Infrastructure/ReferenceData/ReferenceDataSeeder.cs` — fail-fast row parsing (Task 8)
- `src/WealthIQ.Infrastructure/Persistence/Migrations/` — two new migrations (Tasks 2, 9)

**Modified — Application diagnostics enum:**
- `src/WealthIQ.Application/Import/Diagnostic/ImportDiagnosticCode.cs` — add `MalformedField` (Task 1)

**Modified — Web:**
- `src/WealthIQ.Web/Program.cs` — DbContextFactory, config paths, /Error (Tasks 10, 11, 12)
- `src/WealthIQ.Web/Components/Pages/Import.razor` — temp leak, progress total (Tasks 13, 14)
- `src/WealthIQ.Web/Components/Pages/Audit.razor` — query-param refresh, status column (Tasks 9, 15)
- Create: `src/WealthIQ.Web/Components/Pages/Error.razor` (Task 11)
- `src/WealthIQ.Web/appsettings.json` — data-path config keys (Task 12)
- Create: `Directory.Packages.props` (Task 16)

**Modified — Tests (created/updated per task):**
- `tests/WealthIQ.Tests/Infrastructure/Import/IbkrStatementImporterFailFastTests.cs` (new, Task 1)
- `tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteLedgerStoreTests.cs` (Task 2)
- `tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorEdgeCaseTests.cs` (Tasks 3, 6)
- `tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorVorabpauschaleTests.cs` (Tasks 4, 5)
- `tests/WealthIQ.Tests/Application/Matcher/FiFoMatcherTest.cs` (Task 7)
- `tests/WealthIQ.Tests/Infrastructure/ReferenceData/ReferenceDataSeederTests.cs` (Task 8)
- `tests/WealthIQ.Tests/Application/Import/StatementImportPipelineTests.cs` + `Fakes/FakeImportStore.cs` (Task 9)

---

## Baseline check (do this first)

- [ ] **Step 0: Confirm green baseline**

Run: `dotnet test WealthIQ.slnx`
Expected: PASS (review reported 96/96). If anything fails before changes, stop and investigate — do not build fixes on a red baseline.

---

## Task 1: IBKR importer fail-fast on malformed required fields (#2)

**Files:**
- Modify: `src/WealthIQ.Application/Import/Diagnostic/ImportDiagnosticCode.cs`
- Modify: `src/WealthIQ.Infrastructure/Ibkr/Import/IbkrStatementImporter.cs:160-168`, `454-475`
- Test: `tests/WealthIQ.Tests/Infrastructure/Import/IbkrStatementImporterFailFastTests.cs` (new)

**Why:** `ParseDecimal` returns `0m` and `ParseDateTimeOffset` returns `DateTimeOffset.MinValue` on failure, so a record missing `quantity`/`tradePrice`/`amount`/date silently becomes a valid-looking entry with zero values or a `0001-01-01` date. The guardrail requires required-field failures to surface as `Error` diagnostics (which the pipeline already aborts on).

- [ ] **Step 1: Add the new diagnostic code**

In `ImportDiagnosticCode.cs`, add `MalformedField` to the enum:

```csharp
namespace WealthIQ.Application.Import.Diagnostic;

public enum ImportDiagnosticCode
{
    UnsupportedSource,
    InputPathNotFound,
    FileReadFailed,
    InvalidRecord,
    IgnoredAsset,
    CancellationRemoved,
    MalformedField
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/WealthIQ.Tests/Infrastructure/Import/IbkrStatementImporterFailFastTests.cs`:

```csharp
using System.Globalization;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Ibkr.Import;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Import;

/// <summary>
/// A required field that is missing or unparseable must surface as an Error diagnostic and produce
/// no entry — never a zero-valued or 0001-01-01 entry (fail-fast, CLAUDE.md "no silent drops").
/// </summary>
public sealed class IbkrStatementImporterFailFastTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wealthiq-failfast-" + Guid.NewGuid().ToString("N"));

    private async Task<ImportResult> ImportTradeAsync(string tradeElement)
    {
        Directory.CreateDirectory(_temp);
        var path = Path.Combine(_temp, "statement.xml");
        await File.WriteAllTextAsync(path,
            $"""
            <FlexQueryResponse><FlexStatements count="1"><FlexStatement accountId="U1">
            <Trades>{tradeElement}</Trades>
            </FlexStatement></FlexStatements></FlexQueryResponse>
            """);

        return await new IbkrStatementImporter().ImportAsync(new ImportRequest
        {
            AccountId = AccountId.NewId(),
            Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, path)
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Trade_MissingQuantity_EmitsErrorAndProducesNoEntry()
    {
        var result = await ImportTradeAsync(
            """<Trade transactionID="1" assetCategory="STK" symbol="VUSA" isin="IE00B3XXRP09" currency="EUR" buySell="BUY" tradePrice="10" dateTime="20240102;100000" />""");

        Assert.Empty(result.PortfolioLedger.Entries.OfType<TradeEntry>());
        Assert.Contains(result.Diagnostics,
            d => d.Code == ImportDiagnosticCode.MalformedField
              && d.Severity == ImportDiagnosticSeverity.Error
              && d.Field == "quantity");
    }

    [Fact]
    public async Task Trade_UnparseableDate_EmitsErrorAndProducesNoEntry()
    {
        var result = await ImportTradeAsync(
            """<Trade transactionID="1" assetCategory="STK" symbol="VUSA" isin="IE00B3XXRP09" currency="EUR" buySell="BUY" quantity="5" tradePrice="10" dateTime="not-a-date" />""");

        Assert.Empty(result.PortfolioLedger.Entries.OfType<TradeEntry>());
        Assert.Contains(result.Diagnostics,
            d => d.Code == ImportDiagnosticCode.MalformedField && d.Severity == ImportDiagnosticSeverity.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~IbkrStatementImporterFailFastTests"`
Expected: FAIL — currently a zero-quantity / MinValue-date entry is produced and no `MalformedField` diagnostic exists.

- [ ] **Step 4: Replace the parse helpers with Try-style versions**

In `IbkrStatementImporter.cs`, replace the bottom helpers (`ParseDecimal` at `:454-455` and `ParseDateTimeOffset` at `:457-475`):

```csharp
    private static bool TryParseDecimal(string? value, bool allowEmpty, out decimal result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = 0m;
            return allowEmpty;
        }

        return decimal.TryParse(value, NumberStyles.Any, Culture, out result);
    }

    private static bool TryParseDateTimeOffset(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (DateTime.TryParseExact(value, "yyyyMMdd;HHmmss", Culture, DateTimeStyles.AssumeUniversal, out var dateTime))
        {
            result = new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
            return true;
        }

        if (DateTime.TryParseExact(value, "yyyyMMdd", Culture, DateTimeStyles.AssumeUniversal, out var dateOnly))
        {
            result = new DateTimeOffset(DateTime.SpecifyKind(dateOnly.AddHours(23).AddMinutes(59).AddSeconds(59), DateTimeKind.Utc));
            return true;
        }

        return false;
    }
```

- [ ] **Step 5: Validate required fields in `ParseElement`**

In `IbkrStatementImporter.cs`, replace the block at `:160-168` (from `var occurredAt = ParseDateTimeOffset(...)` through `var fees = new Money(...)`) with:

```csharp
        var rawDate = element.Attribute("dateTime")?.Value
            ?? element.Attribute("tradeDate")?.Value
            ?? element.Attribute("reportDate")?.Value;
        if (!TryParseDateTimeOffset(rawDate, out var occurredAt))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportDiagnosticCode.MalformedField,
                $"Missing or unparseable date for transaction '{transactionId}'.",
                SourceReference: transactionId,
                Field: "dateTime"));
            return null;
        }

        var effectiveDate = DateOnly.FromDateTime(occurredAt.UtcDateTime);

        // Cash records carry the value in "amount"; trades in "tradePrice". Both are required.
        var rawPrice = isCash ? element.Attribute("amount")?.Value : element.Attribute("tradePrice")?.Value;
        if (!TryParseDecimal(rawPrice, allowEmpty: false, out var price))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportDiagnosticCode.MalformedField,
                $"Missing or unparseable {(isCash ? "amount" : "tradePrice")} for transaction '{transactionId}'.",
                SourceReference: transactionId,
                Field: isCash ? "amount" : "tradePrice"));
            return null;
        }

        var quantity = 0m;
        if (!isCash && !TryParseDecimal(element.Attribute("quantity")?.Value, allowEmpty: false, out quantity))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportDiagnosticCode.MalformedField,
                $"Missing or unparseable quantity for transaction '{transactionId}'.",
                SourceReference: transactionId,
                Field: "quantity"));
            return null;
        }

        // ibCommission is optional (cash records often omit it) → empty means zero.
        TryParseDecimal(element.Attribute("ibCommission")?.Value, allowEmpty: true, out var commission);
        var currencyCode = ParseCurrency(currency);
        var fees = new Money(Math.Abs(commission), currencyCode);
```

> Note: this removes the old `var quantity = ParseDecimal(...)`, `var price = ParseDecimal(...)`, and `var commission = ParseDecimal(...)` lines. The remaining code (`sourceProvenance`, the cash/trade construction) is unchanged.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~IbkrStatementImporterFailFastTests"`
Expected: PASS

- [ ] **Step 7: Run the full importer + end-to-end suite for regressions**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Import"`
Expected: PASS — existing `IbkrCleanCancellationsTests` and `StatementImportEndToEndTests` still green (their fixtures supply all required fields).

- [ ] **Step 8: Commit**

```bash
git add src/WealthIQ.Application/Import/Diagnostic/ImportDiagnosticCode.cs src/WealthIQ.Infrastructure/Ibkr/Import/IbkrStatementImporter.cs tests/WealthIQ.Tests/Infrastructure/Import/IbkrStatementImporterFailFastTests.cs
git commit -m "fix(import): fail-fast on missing/malformed required IBKR fields"
```

---

## Task 2: De-duplicate source references within one batch + unique index (#1)

**Files:**
- Modify: `src/WealthIQ.Infrastructure/Persistence/SqliteLedgerStore.cs:16-28`
- Modify: `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs:23`
- Test: `tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteLedgerStoreTests.cs`
- Migration: `src/WealthIQ.Infrastructure/Persistence/Migrations/` (new)

**Why:** `SaveLedgerAsync` only checks the DB (`AnyAsync`) for existing references; entries added earlier in the same loop are not yet committed, so two entries in the same ledger sharing `(SourceSystem, SourceRecordReference)` both insert. The EF index is non-unique. Both violate the idempotency guardrail.

- [ ] **Step 1: Write the failing test**

Add to `tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteLedgerStoreTests.cs` (after the existing tests, before the closing brace):

```csharp
    [Fact]
    public async Task SaveLedger_DuplicateSourceReferencesInSameLedger_InsertsOnce()
    {
        using var db = new InMemorySqlite();
        var account = new Account(AccountId.NewId(), "U123");
        var instrument = new Instrument(InstrumentId.NewId(), "US0001", "SPY", "S&P 500", 0.3m);

        // Two distinct entries that share the same (SourceSystem, SourceRecordReference).
        var ledger = new PortfolioLedger(
            new PortfolioEntry[]
            {
                Trade(account.AccountId, instrument.InstrumentId, "DUP", 1),
                Trade(account.AccountId, instrument.InstrumentId, "DUP", 2)
            },
            new[] { instrument },
            new[] { account });

        LedgerSaveResult result;
        await using (var ctx = db.NewContext())
        {
            result = await new SqliteLedgerStore(ctx).SaveLedgerAsync(ledger);
        }

        Assert.Equal(1, result.InsertedEntries);
        Assert.Equal(1, result.SkippedDuplicateEntries);

        await using (var ctx = db.NewContext())
        {
            var loaded = await new SqliteLedgerStore(ctx).LoadLedgerAsync();
            Assert.Single(loaded.Entries);
        }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~SaveLedger_DuplicateSourceReferencesInSameLedger_InsertsOnce"`
Expected: FAIL — currently both rows insert (`InsertedEntries == 2`).

- [ ] **Step 3: Add an in-batch dedup set in `SaveLedgerAsync`**

In `SqliteLedgerStore.cs`, replace the entries loop (`:16-28`) with:

```csharp
        var seenInThisBatch = new HashSet<(string System, string Reference)>();

        foreach (var entry in ledger.Entries)
        {
            var system = entry.SourceProvenance.SourceSystem;
            var reference = entry.SourceProvenance.SourceRecordReference;

            // Dedup within the incoming ledger: AnyAsync below only sees committed rows, not the
            // adds queued earlier in this same loop.
            if (!seenInThisBatch.Add((system, reference))) { skipped++; continue; }

            bool exists = await db.PortfolioEntries
                .AnyAsync(r => r.SourceSystem == system && r.SourceRecordReference == reference, ct);

            if (exists) { skipped++; continue; }

            db.PortfolioEntries.Add(PortfolioEntryMapper.ToRow(entry));
            inserted++;
        }
```

- [ ] **Step 4: Make the index unique (defence in depth)**

In `WealthIqDbContext.cs`, change `:23` from:

```csharp
            e.HasIndex(x => new { x.SourceSystem, x.SourceRecordReference });
```
to:
```csharp
            e.HasIndex(x => new { x.SourceSystem, x.SourceRecordReference }).IsUnique();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~SaveLedger_DuplicateSourceReferencesInSameLedger_InsertsOnce"`
Expected: PASS (the in-memory test uses `EnsureCreated()`, which picks up the unique index immediately).

- [ ] **Step 6: Add the EF migration for the unique index**

Run: `dotnet ef migrations add UniqueSourceReference --project src/WealthIQ.Infrastructure`
Expected: creates `..._UniqueSourceReference.cs` dropping the old index and recreating it `unique: true`. Inspect the generated `Up()` to confirm it targets `IX_PortfolioEntries_SourceSystem_SourceRecordReference`.

- [ ] **Step 7: Run the full persistence suite**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Persistence"`
Expected: PASS — existing idempotency test still green.

- [ ] **Step 8: Commit**

```bash
git add src/WealthIQ.Infrastructure/Persistence/SqliteLedgerStore.cs src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs src/WealthIQ.Infrastructure/Persistence/Migrations tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteLedgerStoreTests.cs
git commit -m "fix(persistence): dedup duplicate source refs within a batch + unique index"
```

---

## Task 3: Missing year-end price becomes a blocking error (#3)

**Files:**
- Modify: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs:204-208`
- Test: `tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorEdgeCaseTests.cs:67-82`

**Why:** When a long fund lot exists and the Basiszins is positive, the year-end price is *required* to compute Vorabpauschale. Currently a missing price silently `continue`s, understating tax. CLAUDE.md: "missing required FX/reference/price data is blocking — no silent fallback."

> **Deliberate test change:** `GermanTaxCalculatorEdgeCaseTests.Calculate_MissingYearEndPrice_SkipsVorabpauschale` currently asserts the *skip* behavior. That test encodes the bug. Per the guardrail (and the reviewer's explicit suggestion), it is replaced with one that expects a blocking exception. This is the one place this plan overrides an existing committed test — recorded here intentionally.

- [ ] **Step 1: Rewrite the existing test to expect a blocking failure**

In `GermanTaxCalculatorEdgeCaseTests.cs`, replace the whole `Calculate_MissingYearEndPrice_SkipsVorabpauschale` method (`:67-82`) with:

```csharp
    [Fact]
    public void Calculate_MissingYearEndPrice_WhenVorabRequired_Throws()
    {
        var instrumentId = InstrumentId.NewId();
        var instruments = new[] { new Instrument(instrumentId, Isin, "VUSA", "Vanguard", 0.30m) };
        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, instrumentId, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "BUY-1")
        ]);

        // Basiszins is positive and a long fund lot is held over year-end, so the year-end price is
        // required. It is not configured → fail-fast (CLAUDE.md: missing required price data is blocking).
        var calculator = Calculator(new FakeBasisInterestRateProvider((2024, 0.05m)), new FakeYearEndPriceProvider());

        Assert.Throws<InvalidOperationException>(() => calculator.Calculate(ledger, instruments));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Calculate_MissingYearEndPrice_WhenVorabRequired_Throws"`
Expected: FAIL — calculator currently returns normally (skips), so `Assert.Throws` fails.

- [ ] **Step 3: Throw on the missing required price**

In `GermanTaxCalculator.cs`, replace `:204-208`:

```csharp
            var yearEndPrice = yearEndPriceProvider.GetPrice(instrument.ISIN, year);
            if (!yearEndPrice.HasValue)
            {
                continue;
            }
```
with:
```csharp
            var yearEndPrice = yearEndPriceProvider.GetPrice(instrument.ISIN, year);
            if (!yearEndPrice.HasValue)
            {
                throw new InvalidOperationException(
                    $"Year-end price for ISIN '{instrument.ISIN}' in {year} is required to compute Vorabpauschale " +
                    $"but is missing. Add it to the reference price data.");
            }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Calculate_MissingYearEndPrice_WhenVorabRequired_Throws"`
Expected: PASS

- [ ] **Step 5: Run the full tax suite + regression for fallout**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Tax"`
Expected: PASS. The Vorabpauschale and regression tests all configure a year-end price for held lots (verified: `GermanTaxCalculatorVorabpauschaleTests` always passes `yearEndPrice`; `GermanTaxRegressionTests` reads `data/test` prices). If the regression test fails because `data/test`/`data/reference` lacks a price for a fund genuinely held over a year-end, that is the bug surfacing — add the missing year-end price row to the committed fixture deliberately and note why in the commit message.

- [ ] **Step 6: Commit**

```bash
git add src/WealthIQ.Application/Tax/GermanTaxCalculator.cs tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorEdgeCaseTests.cs
git commit -m "fix(tax): block on missing year-end price when Vorabpauschale is required"
```

---

## Task 4: Compute Vorabpauschale across quiet (no-activity) years (#4)

**Files:**
- Modify: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs:33-52`
- Test: `tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorVorabpauschaleTests.cs`

**Why:** The replay groups by years present in `Entries` and runs year-end closing only for those years. A fund bought in 2023, held untouched through 2024, sold 2025 never gets a 2024 year-end closing, so the 2025-01-01 Vorabpauschale is missing and the later sale under-deducts previously-taxed Vorabpauschale.

> **Scope note:** this fixes the in-range quiet-year case (the reviewer's example), by closing every year from the first to the last entry year. A position still held *after* the last ledger entry would need an explicit "through year" / as-of parameter — the calculator has none today and `AnnualTaxReportService` generates all years from the result. That residual case is a documented known thin spot, out of scope here.

- [ ] **Step 1: Write the failing test**

Add to `GermanTaxCalculatorVorabpauschaleTests.cs` (before the closing brace). It buys in 2023, holds through a quiet 2024, and asserts a 2025-01-01 Vorabpauschale exists (deemed received for the quiet 2024 year):

```csharp
    [Fact]
    public void Vorabpauschale_QuietHoldingYearWithNoEntries_StillProducesEntry()
    {
        // Buy 2023, no entries at all in 2024, sale would be later. The 2024 year-end closing must still
        // run, posting a Vorabpauschale deemed received 2025-01-01.
        var calculator = new GermanTaxCalculator(
            new FakeBasisInterestRateProvider((2023, 0.05m), (2024, 0.05m)),
            new FakeYearEndPriceProvider((Isin, 2023, 150m), (Isin, 2024, 200m)),
            new FakeFxRateLookup());

        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, Equity, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2023, 1, 10, 10, 0, 0, TimeSpan.Zero), "BUY-1"),
            // A late 2025 entry establishes the replay range end; 2024 has no entries.
            TaxEntries.Trade(Account, Equity, TradeSide.Buy, 1m, 100m,
                new DateTimeOffset(2025, 6, 10, 10, 0, 0, TimeSpan.Zero), "BUY-2")
        ]);

        var result = calculator.Calculate(ledger, Catalog);

        Assert.Contains(result.Entries,
            e => e.Type == GermanTaxEntryType.Vorabpauschale && e.Date == new DateOnly(2025, 1, 1));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Vorabpauschale_QuietHoldingYearWithNoEntries_StillProducesEntry"`
Expected: FAIL — no closing runs for 2024, so no 2025-01-01 entry.

- [ ] **Step 3: Iterate the full year range, not only years with entries**

In `GermanTaxCalculator.cs`, replace the outer loop (`:33-52`):

```csharp
        foreach (var yearlyEntries in portfolioLedger.Entries
                     .OrderBy(x => x.OccurredAt)
                     .GroupBy(x => x.OccurredAt.Year)
                     .OrderBy(x => x.Key))
        {
            foreach (var portfolioEntry in yearlyEntries)
            {
                switch (portfolioEntry)
                {
                    case TradeEntry tradeEntry:
                        ProcessTrade(tradeEntry, openLots, ledger, instrumentById);
                        break;
                    case CashEntry cashEntry:
                        ProcessCash(cashEntry, openLots, ledger, distributions, instrumentById);
                        break;
                }
            }

            PerformYearEndClosing(yearlyEntries.Key, openLots, ledger, distributions, instrumentById);
        }
```
with:
```csharp
        var orderedEntries = portfolioLedger.Entries.OrderBy(x => x.OccurredAt).ToList();
        var entriesByYear = orderedEntries
            .GroupBy(x => x.OccurredAt.Year)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (orderedEntries.Count > 0)
        {
            var firstYear = orderedEntries[0].OccurredAt.Year;
            var lastYear = orderedEntries[^1].OccurredAt.Year;

            // Close every year in the range — including quiet years with no entries — so a Vorabpauschale
            // is posted for each year a lot is held over year-end (CLAUDE.md tax guardrails).
            for (var year = firstYear; year <= lastYear; year++)
            {
                if (entriesByYear.TryGetValue(year, out var yearEntries))
                {
                    foreach (var portfolioEntry in yearEntries)
                    {
                        switch (portfolioEntry)
                        {
                            case TradeEntry tradeEntry:
                                ProcessTrade(tradeEntry, openLots, ledger, instrumentById);
                                break;
                            case CashEntry cashEntry:
                                ProcessCash(cashEntry, openLots, ledger, distributions, instrumentById);
                                break;
                            default:
                                throw new NotSupportedException(
                                    $"Tax replay does not support entry type '{portfolioEntry.GetType().Name}'.");
                        }
                    }
                }

                PerformYearEndClosing(year, openLots, ledger, distributions, instrumentById);
            }
        }
```

> The `default:` arm is Task 6's fail-fast guard, folded in here since this is the switch that needs it.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Vorabpauschale_QuietHoldingYearWithNoEntries_StillProducesEntry"`
Expected: PASS

- [ ] **Step 5: Run the full tax suite**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Tax"`
Expected: PASS — single-year tests are unaffected (range of one year == old behavior).

- [ ] **Step 6: Commit**

```bash
git add src/WealthIQ.Application/Tax/GermanTaxCalculator.cs tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorVorabpauschaleTests.cs
git commit -m "fix(tax): run year-end Vorabpauschale closing across quiet holding years"
```

---

## Task 5: Allocate dividend reductions by account and holding interval (#5)

**Files:**
- Modify: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs:31`, `114-148`, `179-244`
- Test: `tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorVorabpauschaleTests.cs`

**Why:** Distributions are stored as one per-share value per `(year, instrument)` and subtracted from *every* remaining lot at year-end. Lots acquired *after* the dividend ex-date receive a reduction they shouldn't, and the calculation ignores account boundaries, so a dividend in one account reduces Vorabpauschale for lots in another.

- [ ] **Step 1: Write the failing tests**

Add to `GermanTaxCalculatorVorabpauschaleTests.cs`. The first proves a lot bought after the dividend is *not* reduced; the second proves cross-account isolation:

```csharp
    [Fact]
    public void Vorabpauschale_LotBoughtAfterDividend_IsNotReducedByThatDividend()
    {
        // Lot A (Jan) is held at the June dividend; lot B (Aug) is not. Only A's Vorabpauschale is
        // reduced by the distribution. With a per-share basis yield of 3.50 and a 3.00/share dividend
        // allocated only to A's 100 shares: A → max(0, 3.50-3.00)=0.50/sh, B → full 3.50/sh.
        var calculator = Calculator(basisRate: 0.05m, yearEndPrice: 200m);
        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, Equity, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "BUY-A"),
            TaxEntries.Dividend(Account, Equity, Equity, grossAmount: 300m,
                new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero), "DIV-1"),
            TaxEntries.Trade(Account, Equity, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2024, 8, 10, 10, 0, 0, TimeSpan.Zero), "BUY-B")
        ]);

        var result = calculator.Calculate(ledger, Catalog);

        var vorab = result.Entries.Where(x => x.Type == GermanTaxEntryType.Vorabpauschale).ToList();
        var total = vorab.Sum(v => v.RawAmount);
        // A: 0.50 × 100 = 50.00 ; B: 3.50 × 100 = 350.00 ; total = 400.00
        Assert.Equal(400m, decimal.Round(total, 2));
    }

    [Fact]
    public void Vorabpauschale_DividendInOtherAccount_DoesNotReduceThisAccount()
    {
        var otherAccount = AccountId.NewId();
        // Same instrument held in two accounts; the dividend is paid in `otherAccount` only.
        var calculator = Calculator(basisRate: 0.05m, yearEndPrice: 200m);
        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, Equity, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "BUY-1"),
            TaxEntries.Trade(otherAccount, Equity, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "BUY-2"),
            TaxEntries.Dividend(otherAccount, Equity, Equity, grossAmount: 1000m,
                new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero), "DIV-1")
        ]);

        var result = calculator.Calculate(ledger, Catalog);

        // `Account`'s lot received no distribution → full basis-yield Vorabpauschale: 3.50 × 100 = 350.00.
        var total = result.Entries.Where(x => x.Type == GermanTaxEntryType.Vorabpauschale).Sum(v => v.RawAmount);
        Assert.Equal(350m, decimal.Round(total, 2));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Vorabpauschale_LotBoughtAfterDividend_IsNotReducedByThatDividend|FullyQualifiedName~Vorabpauschale_DividendInOtherAccount_DoesNotReduceThisAccount"`
Expected: FAIL — current code over-reduces across all lots/accounts.

- [ ] **Step 3: Replace the distributions store with per-(account,instrument,date) records**

In `GermanTaxCalculator.cs`, at `:31` replace:

```csharp
        var distributions = new Dictionary<(int Year, InstrumentId InstrumentId), decimal>();
```
with:
```csharp
        var distributions = new List<Distribution>();
```

Add this nested type at the bottom of the class (before the final closing brace):

```csharp
    /// <summary>A per-share distribution recorded for Vorabpauschale reduction, scoped to the account,
    /// instrument and the date it was paid (so only lots held at that date are reduced).</summary>
    private readonly record struct Distribution(
        int Year,
        AccountId AccountId,
        InstrumentId InstrumentId,
        DateOnly Date,
        decimal PerShare);
```

- [ ] **Step 4: Record distributions scoped to the paying account**

In `ProcessCash`, change the signature parameter type and the `CashFlowType.Dividend` branch (`:114-148`). Replace the method signature line:

```csharp
        Dictionary<(int Year, InstrumentId InstrumentId), decimal> distributions,
```
with:
```csharp
        List<Distribution> distributions,
```

Then replace the dividend `heldLots`/`distributions` block (`:137-147`) with:

```csharp
                var heldLots = openLots
                    .Where(x => x.AccountId == cashEntry.AccountId
                        && x.InstrumentId == dividendInstrument.InstrumentId
                        && x.RemainingQuantity.Value > 0m)
                    .ToList();

                var totalHeldQuantity = heldLots.Sum(x => x.RemainingQuantity.Value);
                if (totalHeldQuantity > 0m)
                {
                    var dividendPerShare = rawDividend / totalHeldQuantity;
                    distributions.Add(new Distribution(
                        cashEntry.OccurredAt.Year,
                        cashEntry.AccountId,
                        dividendInstrument.InstrumentId,
                        date,
                        dividendPerShare));
                }
                break;
```

- [ ] **Step 5: Apply per-lot reduction using account + holding interval**

In `PerformYearEndClosing`, change the signature parameter type:

```csharp
        Dictionary<(int Year, InstrumentId InstrumentId), decimal> distributions,
```
to:
```csharp
        List<Distribution> distributions,
```

Then replace the `distributionPerShare` lookup (`:210`) and the per-lot use (`:223`). Replace this line at `:210`:

```csharp
            var distributionPerShare = distributions.GetValueOrDefault((year, instrument.InstrumentId));
```
with nothing (delete it — the per-share value is now computed per lot inside the loop). Then, inside `foreach (var lot in instrumentGroup.ToList())`, after `var acquisitionPrice = ...` compute the lot-specific reduction. Replace `:223`:

```csharp
                var actualVorabpauschalePerShare = Math.Max(0m, maxVorabpauschale - distributionPerShare);
```
with:
```csharp
                // Only distributions paid into THIS lot's account, on THIS instrument, while the lot
                // was already held (paid on/after the lot's open date), reduce its Vorabpauschale.
                var distributionPerShare = distributions
                    .Where(d => d.Year == year
                        && d.AccountId == lot.AccountId
                        && d.InstrumentId == instrument.InstrumentId
                        && d.Date >= lot.OpenTradeDate)
                    .Sum(d => d.PerShare);

                var actualVorabpauschalePerShare = Math.Max(0m, maxVorabpauschale - distributionPerShare);
```

> The `instrumentGroup` grouping by `InstrumentId` still works because the per-lot filter narrows by `lot.AccountId`. No change to the grouping itself.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Vorabpauschale_LotBoughtAfterDividend_IsNotReducedByThatDividend|FullyQualifiedName~Vorabpauschale_DividendInOtherAccount_DoesNotReduceThisAccount"`
Expected: PASS

- [ ] **Step 7: Run the full tax suite**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Tax"`
Expected: PASS — the existing `Vorabpauschale_DistributionExceedsBasisYield_ProducesNoEntryButKeepsDividend` test (dividend on the same single account, lot held before the dividend) still reduces to zero. If the `GermanTaxRegressionTests` 2024 figures shift, that is a real correction — re-derive the expected disposal/Vorabpauschale values from the statute, update them, and explain the change in the commit message (CLAUDE.md golden-baseline rule).

- [ ] **Step 8: Commit**

```bash
git add src/WealthIQ.Application/Tax/GermanTaxCalculator.cs tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorVorabpauschaleTests.cs
git commit -m "fix(tax): allocate dividend Vorabpauschale reduction by account and holding interval"
```

---

## Task 6: Fail-fast on unsupported entry types in tax replay (#6)

**Files:**
- Modify: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs` (switch `default:` — already added in Task 4 Step 3)
- Test: `tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorEdgeCaseTests.cs`

**Why:** `AssetTransferEntry` and `PositionAdjustmentEntry` exist in the domain but the replay `switch` only handles `TradeEntry`/`CashEntry`, silently ignoring them. No importer constructs these today (verified — only a domain test references `AssetTransferEntry`), so full transfer/adjustment tax semantics are YAGNI. The correct minimal fix is to fail loudly rather than silently skip, matching the fail-fast guardrail. The `default:` throw was added in Task 4 Step 3; this task pins it with a test.

- [ ] **Step 1: Write the failing test**

Add to `GermanTaxCalculatorEdgeCaseTests.cs`. It builds an `AssetTransferEntry` and asserts the calculator throws rather than ignoring it. Check the `AssetTransferEntry` constructor signature first (`src/WealthIQ.Domain/Model/Ledger/AssetTransferEntry.cs`) and supply valid arguments; the shape below mirrors the other entries (id, account, occurredAt, effectiveDate, provenance, …):

```csharp
    [Fact]
    public void Calculate_UnsupportedEntryType_Throws()
    {
        var instrumentId = InstrumentId.NewId();
        var instruments = new[] { new Instrument(instrumentId, Isin, "VUSA", "Vanguard", 0.30m) };

        // AssetTransferEntry is a valid canonical entry no importer currently produces. Tax replay
        // must not silently ignore it — it must fail fast.
        var transfer = new AssetTransferEntry(
            PortfolioEntryId.NewId(),
            Account,
            new DateTimeOffset(2024, 4, 1, 10, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 4, 1),
            TaxEntries.Provenance("XFER-1"),
            instrumentId,
            new Quantity(10m));

        var ledger = new PortfolioLedger([transfer]);

        Assert.Throws<NotSupportedException>(() => Calculator().Calculate(ledger, instruments));
    }
```

> If the real `AssetTransferEntry` constructor differs, adjust the arguments to match — the test's point is solely that an unsupported entry type reaches `Calculate` and throws `NotSupportedException`. Add `using WealthIQ.Domain.Model.Lot;` if `Quantity` is not already imported (it is via `WealthIQ.Domain.Model.General`).

- [ ] **Step 2: Run test to verify it passes (guard already added in Task 4)**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Calculate_UnsupportedEntryType_Throws"`
Expected: PASS — the `default: throw new NotSupportedException(...)` arm added in Task 4 Step 3 handles it. If this is implemented before Task 4, add that `default:` arm to the replay switch now.

- [ ] **Step 3: Commit**

```bash
git add tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorEdgeCaseTests.cs
git commit -m "test(tax): pin fail-fast on unsupported canonical entry types in replay"
```

---

## Task 7: Deterministic FIFO ordering for same-timestamp lots (#10)

**Files:**
- Modify: `src/WealthIQ.Domain/Model/Lot/OpenLot.cs`
- Modify: `src/WealthIQ.Application/Matcher/FiFoMatcher.cs:21-22`, `66-80`
- Modify: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs:268-283` (`CreateLongLot`)
- Test: `tests/WealthIQ.Tests/Application/Matcher/FiFoMatcherTest.cs`

**Why:** `PortfolioLedger` orders same-timestamp entries by `SourceRecordReference`, but `FiFoMatcher` re-sorts open lots with the *unstable* `List<T>.Sort` keyed only on `OpenOccurredAt`, and `OpenLot` carries no source reference. Two same-timestamp buys at different prices can be consumed in a non-broker order during a later partial sale, changing realized gains — breaking the determinism guarantee.

- [ ] **Step 1: Add a tie-break field to `OpenLot`**

In `OpenLot.cs`, after `public DateOnly OpenTradeDate { get; init; }` (`:16`) add:

```csharp
    /// <summary>The opening trade's source record reference (e.g. broker transaction id). Used as the
    /// deterministic FIFO tie-break for lots that share <see cref="OpenOccurredAt"/>, mirroring
    /// <c>PortfolioLedger</c> ordering. Defaults to empty for lots built without provenance.</summary>
    public string OpenSourceReference { get; init; } = "";
```

- [ ] **Step 2: Write the failing test**

Add to `tests/WealthIQ.Tests/Application/Matcher/FiFoMatcherTest.cs`. Two same-timestamp buys with different prices and source refs, then a partial sale consuming exactly the first lot; assert the lower-ref lot is consumed first:

```csharp
    [Fact]
    public void Match_SameTimestampBuys_ConsumesInSourceReferenceOrder()
    {
        var account = AccountId.NewId();
        var instrument = InstrumentId.NewId();
        var ts = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero);

        // Two buys at the SAME timestamp, different prices and source references.
        var lotA = new OpenLot
        {
            LotId = LotId.NewId(), AccountId = account, InstrumentId = instrument,
            OpenEntryId = PortfolioEntryId.NewId(), OpenOccurredAt = ts, OpenTradeDate = DateOnly.FromDateTime(ts.UtcDateTime),
            OpenSourceReference = "A-1", Direction = PositionDirection.Long,
            OriginalQuantity = new Quantity(10m), RemainingQuantity = new Quantity(10m),
            OpenUnitPrice = new Money(100m, Currency.EUR), RemainingOpenFees = new Money(0m, Currency.EUR),
            RemainingOpenTaxes = new Money(0m, Currency.EUR)
        };
        var lotB = lotA with { LotId = LotId.NewId(), OpenSourceReference = "B-2", OpenUnitPrice = new Money(200m, Currency.EUR) };

        // Pass them in reverse order to prove the matcher re-establishes deterministic order.
        var sell = new TradeEntry(
            PortfolioEntryId.NewId(), account, ts.AddDays(1), DateOnly.FromDateTime(ts.AddDays(1).UtcDateTime),
            new SourceProvenance { SourceSystem = "IBKR", ImportFormat = "XML", SourceLocation = "f", SourceRecordReference = "SELL" },
            instrument, TradeSide.Sell, new Quantity(10m),
            new Money(300m, Currency.EUR), new Money(0m, Currency.EUR), new Money(0m, Currency.EUR));

        var result = new FiFoMatcher().Match(sell, new[] { lotB, lotA }, LotMatchingPolicy.FIFO);

        var consumption = Assert.Single(result.Consumptions);
        Assert.Equal(lotA.LotId, consumption.OpenLotId);          // A-1 consumed before B-2
        Assert.Equal(100m, consumption.OpenUnitPrice.Amount);
    }
```

> Add any missing `using` directives at the top of the test file: `WealthIQ.Domain.Enumeration; WealthIQ.Domain.Model.General; WealthIQ.Domain.Model.Ledger; WealthIQ.Domain.Model.Lot; WealthIQ.Domain.Model.Matching;` and `WealthIQ.Application.Matcher;`.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Match_SameTimestampBuys_ConsumesInSourceReferenceOrder"`
Expected: FAIL (or flaky) — unstable `Sort` may consume B-2 first.

- [ ] **Step 4: Replace the unstable sort with a stable, tie-broken ordering**

In `FiFoMatcher.cs`, replace `:21-22`:

```csharp
        var updateOpenLots = currentOpenLots.ToList();
        updateOpenLots.Sort((x, y) => x.OpenOccurredAt.CompareTo(y.OpenOccurredAt));
```
with:
```csharp
        // Stable, deterministic FIFO order: by open time, then by the opening source reference
        // (broker booking order), mirroring PortfolioLedger's tie-break. Avoids List.Sort's instability.
        var updateOpenLots = currentOpenLots
            .OrderBy(x => x.OpenOccurredAt)
            .ThenBy(x => x.OpenSourceReference, StringComparer.Ordinal)
            .ToList();
```

- [ ] **Step 5: Populate `OpenSourceReference` when the matcher opens a remainder lot**

In `FiFoMatcher.cs`, in the `newOpenLot` initializer (`:66-80`), after `OpenTradeDate = DateOnly.FromDateTime(tradeEntry.OccurredAt.DateTime),` add:

```csharp
                OpenSourceReference = tradeEntry.SourceProvenance.SourceRecordReference,
```

- [ ] **Step 6: Populate `OpenSourceReference` in `CreateLongLot`**

In `GermanTaxCalculator.cs`, in `CreateLongLot` (`:268-283`), after `OpenTradeDate = DateOnly.FromDateTime(tradeEntry.OccurredAt.UtcDateTime),` add:

```csharp
        OpenSourceReference = tradeEntry.SourceProvenance.SourceRecordReference,
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Match_SameTimestampBuys_ConsumesInSourceReferenceOrder"`
Expected: PASS

- [ ] **Step 8: Run the matcher + tax suites**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Matcher|FullyQualifiedName~Tax"`
Expected: PASS — existing distinct-timestamp ordering is unchanged.

- [ ] **Step 9: Commit**

```bash
git add src/WealthIQ.Domain/Model/Lot/OpenLot.cs src/WealthIQ.Application/Matcher/FiFoMatcher.cs src/WealthIQ.Application/Tax/GermanTaxCalculator.cs tests/WealthIQ.Tests/Application/Matcher/FiFoMatcherTest.cs
git commit -m "fix(matcher): deterministic FIFO tie-break for same-timestamp lots"
```

---

## Task 8: Reference-data seeding fails fast on malformed rows (#9)

**Files:**
- Modify: `src/WealthIQ.Infrastructure/ReferenceData/ReferenceDataSeeder.cs:52-87`, `111-130`
- Test: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/ReferenceDataSeederTests.cs`

**Why:** The CSV readers `yield` only on successful parse and silently skip rows with too few columns; FX rows with non-positive rates are silently dropped. Committed seed data can be incomplete while startup succeeds, surfacing later as a "missing FX/price/rate" failure. `ReadInstrumentProfiles` already throws — the CSV readers should be consistent (fail-fast for required reference data).

- [ ] **Step 1: Write the failing test**

Add to `tests/WealthIQ.Tests/Infrastructure/ReferenceData/ReferenceDataSeederTests.cs`. It writes an FX CSV with one malformed row and asserts seeding throws with the file path and line number. Reuse the file's existing helper patterns for building `ReferenceDataSources` / a temp dir; if none exist, write temp files inline:

```csharp
    [Fact]
    public async Task SeedIfEmpty_MalformedFxRow_ThrowsWithFileAndLine()
    {
        using var db = new InMemorySqlite();
        var dir = Path.Combine(Path.GetTempPath(), "wealthiq-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Header + one good row + one malformed row (non-numeric rate) on line 3.
            var fxPath = Path.Combine(dir, "fx_rates.csv");
            await File.WriteAllTextAsync(fxPath, "date,currency,rate\n2024-01-02,USD,0.91\n2024-01-03,USD,not-a-rate\n");

            // Minimal valid files for the other three sources so FX is what fails.
            var basisPath = Path.Combine(dir, "basiszins.csv");
            await File.WriteAllTextAsync(basisPath, "year,rate\n2024,0.0255\n");
            var pricesPath = Path.Combine(dir, "prices.csv");
            await File.WriteAllTextAsync(pricesPath, "year,isin,price\n2024,IE00B3XXRP09,200\n");
            var instrumentsPath = Path.Combine(dir, "instruments.json");
            await File.WriteAllTextAsync(instrumentsPath, "{}");

            var sources = new ReferenceDataSources(basisPath, pricesPath, instrumentsPath, fxPath);

            await using var ctx = db.NewContext();
            var seeder = new ReferenceDataSeeder(ctx);

            var ex = await Assert.ThrowsAsync<FormatException>(() => seeder.SeedIfEmptyAsync(sources));
            Assert.Contains("fx_rates.csv", ex.Message);
            Assert.Contains("line 3", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
```

> Add `using WealthIQ.Application.ReferenceData; using WealthIQ.Infrastructure.ReferenceData;` and the `InMemorySqlite` namespace if not present. Confirm the `ReferenceDataSources` constructor order against `src/WealthIQ.Application/ReferenceData/ReferenceDataSources.cs` (basis, prices, instruments, fx — matching `Program.cs:70-74`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~SeedIfEmpty_MalformedFxRow_ThrowsWithFileAndLine"`
Expected: FAIL — the malformed row is silently skipped; no exception.

- [ ] **Step 3: Make `ReadCsv` track line numbers and the readers throw on malformed rows**

In `ReferenceDataSeeder.cs`, replace `ReadCsv` (`:111-131`) to yield `(int LineNumber, string[] Parts)` and throw on too-few columns:

```csharp
    private static IEnumerable<(int LineNumber, string[] Parts)> ReadCsv(string path, string notFoundMessage, int minColumns)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(notFoundMessage, path);
        }

        var lineNumber = 1; // header is line 1
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length < minColumns)
            {
                throw new FormatException(
                    $"Malformed row in '{Path.GetFileName(path)}' line {lineNumber}: expected at least {minColumns} columns, got {parts.Length}.");
            }

            yield return (lineNumber, parts);
        }
    }
```

- [ ] **Step 4: Throw on unparseable values in each reader**

Replace `ReadBasisInterestRates` (`:52-62`):

```csharp
    private static IEnumerable<BasisInterestRateRow> ReadBasisInterestRates(string path)
    {
        foreach (var (lineNumber, parts) in ReadCsv(path, "Basis interest rate file not found.", minColumns: 2))
        {
            if (!int.TryParse(parts[0], out var year)
                || !decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
            {
                throw new FormatException($"Malformed row in '{Path.GetFileName(path)}' line {lineNumber}: invalid year or rate.");
            }

            yield return new BasisInterestRateRow { Year = year, Rate = rate };
        }
    }
```

Replace `ReadYearEndPrices` (`:64-74`):

```csharp
    private static IEnumerable<YearEndPriceRow> ReadYearEndPrices(string path)
    {
        foreach (var (lineNumber, parts) in ReadCsv(path, "Year-end price file not found.", minColumns: 3))
        {
            if (!int.TryParse(parts[0], out var year)
                || !decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
            {
                throw new FormatException($"Malformed row in '{Path.GetFileName(path)}' line {lineNumber}: invalid year or price.");
            }

            yield return new YearEndPriceRow { Year = year, Isin = parts[1].Trim(), PriceEur = price };
        }
    }
```

Replace `ReadFxRates` (`:76-87`):

```csharp
    private static IEnumerable<FxRateRow> ReadFxRates(string path)
    {
        foreach (var (lineNumber, parts) in ReadCsv(path, "FX rate file not found.", minColumns: 3))
        {
            if (!DateOnly.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rate)
                || rate <= 0m)
            {
                throw new FormatException($"Malformed row in '{Path.GetFileName(path)}' line {lineNumber}: invalid date or non-positive rate.");
            }

            yield return new FxRateRow { Date = date, Currency = parts[1].Trim(), RateToEur = rate };
        }
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~SeedIfEmpty_MalformedFxRow_ThrowsWithFileAndLine"`
Expected: PASS

- [ ] **Step 6: Run the full reference-data suite + verify committed seed data still parses**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~ReferenceData"`
Expected: PASS. If a real committed file under `data/reference/` now throws, that file has a malformed row that was previously silently dropped — fix the data file deliberately and note it.

- [ ] **Step 7: Commit**

```bash
git add src/WealthIQ.Infrastructure/ReferenceData/ReferenceDataSeeder.cs tests/WealthIQ.Tests/Infrastructure/ReferenceData/ReferenceDataSeederTests.cs
git commit -m "fix(seed): fail-fast with file+line on malformed reference-data rows"
```

---

## Task 9: Persist a failed-import batch + diagnostics for the audit trail (#11)

**Files:**
- Modify: `src/WealthIQ.Application/Import/ImportBatch.cs`
- Modify: `src/WealthIQ.Application/Audit/ImportBatchView.cs`
- Modify: `src/WealthIQ.Application/Persistence/Interface/IImportStore.cs`
- Modify: `src/WealthIQ.Application/Import/StatementImportPipeline.cs:43-49`
- Modify: `src/WealthIQ.Infrastructure/Persistence/Rows/ImportBatchRow.cs`
- Modify: `src/WealthIQ.Infrastructure/Persistence/Mapping/ImportBatchMapper.cs`
- Modify: `src/WealthIQ.Infrastructure/Persistence/SqliteImportStore.cs`
- Modify: `src/WealthIQ.Infrastructure/Persistence/SqliteImportAuditStore.cs`
- Modify: `src/WealthIQ.Web/Components/Pages/Audit.razor`
- Modify: `tests/WealthIQ.Tests/Application/Import/Fakes/FakeImportStore.cs`
- Modify: `tests/WealthIQ.Tests/Application/Import/StatementImportPipelineTests.cs`
- Migration: `src/WealthIQ.Infrastructure/Persistence/Migrations/` (new)

**Why (decision):** When blocking diagnostics abort an import, the pipeline currently writes nothing — no audit trail survives a refresh. Decision taken: persist a `Failed` batch (no ledger entries) plus its diagnostics so the Audit page shows failed attempts.

- [ ] **Step 1: Add a status to the `ImportBatch` domain record**

In `ImportBatch.cs`, add a status enum and field:

```csharp
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Application.Import;

public enum ImportBatchStatus
{
    Committed,
    Failed
}

/// <summary>One persisted import run. Failed batches carry diagnostics but no ledger entries.</summary>
public sealed record ImportBatch(
    Guid BatchId,
    Broker Broker,
    Format Format,
    AccountId AccountId,
    string RawFilePath,
    DateTimeOffset ImportedAt,
    ImportBatchStatus Status = ImportBatchStatus.Committed);
```

- [ ] **Step 2: Add `Status` to the row, mapper, and view**

In `ImportBatchRow.cs`, add:

```csharp
    public string Status { get; set; } = "Committed";
```

In `ImportBatchMapper.cs`, add to the `ToRow` initializer:

```csharp
        Status = batch.Status.ToString(),
```

In `ImportBatchView.cs`, add a `string Status` parameter at the end:

```csharp
public sealed record ImportBatchView(
    Guid BatchId,
    string Broker,
    string Format,
    Guid AccountId,
    string RawFilePath,
    DateTimeOffset ImportedAt,
    int InsertedEntries,
    int SkippedDuplicateEntries,
    string Status);
```

In `SqliteImportAuditStore.cs`, pass it through in the `Select`:

```csharp
            .Select(x => new ImportBatchView(
                x.BatchId, x.Broker, x.Format, x.AccountId, x.RawFilePath, x.ImportedAt,
                x.InsertedEntries, x.SkippedDuplicateEntries, x.Status))
```

- [ ] **Step 3: Add `PersistFailedImportAsync` to the store port**

In `IImportStore.cs`, add the method to the interface:

```csharp
    /// <summary>Persists a batch that aborted on blocking diagnostics: the batch row (status Failed)
    /// and its diagnostics, but no ledger entries. Transactional.</summary>
    Task PersistFailedImportAsync(
        ImportBatch batch,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        CancellationToken ct = default);
```

- [ ] **Step 4: Implement it in `SqliteImportStore`**

In `SqliteImportStore.cs`, add the method to the class:

```csharp
    public async Task PersistFailedImportAsync(
        ImportBatch batch,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.ImportBatches.Add(ImportBatchMapper.ToRow(batch));

        foreach (var diagnostic in diagnostics)
        {
            db.ImportDiagnostics.Add(ImportDiagnosticMapper.ToRow(diagnostic, batch.BatchId));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
```

- [ ] **Step 5: Update the `FakeImportStore` test double**

In `tests/WealthIQ.Tests/Application/Import/Fakes/FakeImportStore.cs`, add fields and implement the new method (preserving the existing `CallCount`/`SeenBatch`/`SeenLedger` members). Add:

```csharp
    public int FailedCallCount { get; private set; }
    public ImportBatch? SeenFailedBatch { get; private set; }
    public IReadOnlyList<ImportDiagnostic>? SeenFailedDiagnostics { get; private set; }

    public Task PersistFailedImportAsync(
        ImportBatch batch,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        CancellationToken ct = default)
    {
        FailedCallCount++;
        SeenFailedBatch = batch;
        SeenFailedDiagnostics = diagnostics;
        return Task.CompletedTask;
    }
```

> Add `using WealthIQ.Application.Import.Diagnostic;` to the fake if not already present.

- [ ] **Step 6: Write the failing pipeline test**

In `StatementImportPipelineTests.cs`, replace the body of `Run_BlockingDiagnostic_AbortsWithoutPersisting` so it now asserts the failed batch *is* persisted (rename for clarity):

```csharp
    [Fact]
    public async Task Run_BlockingDiagnostic_PersistsFailedBatchWithoutLedger()
    {
        var result = new ImportResult
        {
            PortfolioLedger = new PortfolioLedger(new PortfolioEntry[] { Trade("T-1") }),
            Diagnostics = { new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.InvalidRecord, "bad record") }
        };
        var store = new FakeImportStore(new ImportPersistCounts(0, 0, 0));
        var pipeline = Build(result, store, out _, out _);

        var outcome = await pipeline.RunAsync(Command());

        Assert.Equal(ImportStatus.Aborted, outcome.Status);
        Assert.Equal(0, outcome.InsertedEntries);
        Assert.Equal(0, store.CallCount);                       // no committed (ledger) persist
        Assert.Equal(1, store.FailedCallCount);                 // failed batch persisted
        Assert.Equal(ImportBatchStatus.Failed, store.SeenFailedBatch!.Status);
        Assert.Single(store.SeenFailedDiagnostics!);            // diagnostics persisted for audit
        Assert.Single(outcome.Diagnostics);                     // and still surfaced to the caller
    }
```

- [ ] **Step 7: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Run_BlockingDiagnostic_PersistsFailedBatchWithoutLedger"`
Expected: FAIL — pipeline returns before persisting; `FailedCallCount == 0`.

- [ ] **Step 8: Persist the failed batch in the pipeline**

In `StatementImportPipeline.cs`, replace the blocking branch (`:45-49`):

```csharp
        var hasBlocking = importResult.Diagnostics.Any(d => d.Severity >= ImportDiagnosticSeverity.Error);
        if (hasBlocking)
        {
            return new ImportPipelineResult(ImportStatus.Aborted, batchId, 0, 0, importResult.Diagnostics);
        }
```
with:
```csharp
        var hasBlocking = importResult.Diagnostics.Any(d => d.Severity >= ImportDiagnosticSeverity.Error);
        if (hasBlocking)
        {
            var failedBatch = new ImportBatch(
                batchId,
                command.Request.Source.Broker,
                command.Request.Source.Format,
                command.Request.AccountId,
                storedPath,
                importedAt,
                ImportBatchStatus.Failed);

            await importStore.PersistFailedImportAsync(failedBatch, importResult.Diagnostics, ct);

            return new ImportPipelineResult(ImportStatus.Aborted, batchId, 0, 0, importResult.Diagnostics);
        }
```

- [ ] **Step 9: Run test to verify it passes**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Run_BlockingDiagnostic_PersistsFailedBatchWithoutLedger"`
Expected: PASS

- [ ] **Step 10: Show status on the Audit page**

In `Audit.razor`, add a status column. In `<HeaderContent>` of the batches table (after `<MudTh>Format</MudTh>`, `:57`) add:

```razor
        <MudTh>Status</MudTh>
```
and in `<RowTemplate>` (after the Format `<MudTd>`, `:64`) add:

```razor
        <MudTd DataLabel="Status">@context.Status</MudTd>
```

- [ ] **Step 11: Add the EF migration for the status column**

Run: `dotnet ef migrations add ImportBatchStatus --project src/WealthIQ.Infrastructure`
Expected: adds a `Status` TEXT column to `ImportBatches` with default. Inspect `Up()` to confirm `AddColumn<string>("Status", "ImportBatches", ...)`.

- [ ] **Step 12: Run the import + persistence suites**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Import|FullyQualifiedName~Persistence"`
Expected: PASS — `SqliteImportAuditStoreTests` still green with the extra view field.

- [ ] **Step 13: Commit**

```bash
git add src/WealthIQ.Application/Import/ImportBatch.cs src/WealthIQ.Application/Audit/ImportBatchView.cs src/WealthIQ.Application/Persistence/Interface/IImportStore.cs src/WealthIQ.Application/Import/StatementImportPipeline.cs src/WealthIQ.Infrastructure/Persistence/Rows/ImportBatchRow.cs src/WealthIQ.Infrastructure/Persistence/Mapping/ImportBatchMapper.cs src/WealthIQ.Infrastructure/Persistence/SqliteImportStore.cs src/WealthIQ.Infrastructure/Persistence/SqliteImportAuditStore.cs src/WealthIQ.Infrastructure/Persistence/Migrations src/WealthIQ.Web/Components/Pages/Audit.razor tests/WealthIQ.Tests/Application/Import/Fakes/FakeImportStore.cs tests/WealthIQ.Tests/Application/Import/StatementImportPipelineTests.cs
git commit -m "feat(import): persist failed-import batch + diagnostics for audit trail"
```

---

## Task 10: Use `IDbContextFactory` for circuit-safe DbContext lifetime (#7)

**Files:**
- Modify: `src/WealthIQ.Web/Program.cs:35-41`
- (Stores already take `WealthIqDbContext` by constructor — they stay unchanged; we change how the context is provided per scope.)

**Why:** `AddDbContext` registers the context as scoped, which in Blazor Server means *circuit*-scoped (lifetime of the user's connection), not per-operation. Concurrent UI actions or overlapping import/report operations share one `DbContext`, risking EF concurrency errors and tracked-state leakage. The standard fix is `AddDbContextFactory` and a fresh context per operation.

> **Design:** The stores (`SqliteLedgerStore`, `SqliteImportStore`, `SqliteImportAuditStore`, the Db reference providers) take `WealthIqDbContext` in their constructors. Rather than rewrite every store to take a factory, register the context as **scoped but created from the factory per scope**, and have each store operation run in its own DI scope created by the Blazor page. The lowest-risk change that satisfies the finding is: register `AddDbContextFactory` AND a scoped `WealthIqDbContext` resolved from the factory, so each component that creates its own scope gets a fresh context. Pages already use injected scoped services per circuit; to get true per-operation isolation we add `AddPooledDbContextFactory` and resolve a fresh context inside store calls. Choose the pragmatic middle ground below.

- [ ] **Step 1: Register a DbContext factory and a per-scope context from it**

In `Program.cs`, replace `:35`:

```csharp
builder.Services.AddDbContext<WealthIqDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
```
with:
```csharp
// Blazor Server: scoped == circuit-lifetime, so a single AddDbContext would be shared across
// overlapping UI operations. Register a factory and resolve a fresh, short-lived context per scope.
builder.Services.AddDbContextFactory<WealthIqDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<WealthIqDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<WealthIqDbContext>>().CreateDbContext());
```

> `AddDbContextFactory` registers the factory as singleton; the scoped registration yields a new context per DI scope. The startup migrate/seed block (`:64-76`) creates its own scope and so gets its own context — unchanged. Stores keep their `WealthIqDbContext` constructor dependency.

- [ ] **Step 2: Build and run the full suite**

Run: `dotnet build WealthIQ.slnx && dotnet test WealthIQ.slnx`
Expected: PASS — tests construct `WealthIqDbContext` directly via `InMemorySqlite`, so they are unaffected; the change is composition-root only.

- [ ] **Step 3: Manually verify the app still starts and imports**

Run: `dotnet run --project src/WealthIQ.Web`
Expected: app starts, migrates, seeds; the Import and Steuerreport pages load and an import commits without an EF "context is being used by a second operation" error.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Program.cs
git commit -m "fix(web): provide DbContext via factory to avoid circuit-scoped sharing"
```

---

## Task 11: Add the missing `/Error` page (#12)

**Files:**
- Create: `src/WealthIQ.Web/Components/Pages/Error.razor`
- (No change to `Program.cs:78-80` — the handler already targets `/Error`.)

**Why:** In non-Development environments `app.UseExceptionHandler("/Error")` routes to a page that does not exist, so production errors 404 instead of showing a useful message.

- [ ] **Step 1: Create the Error page**

Create `src/WealthIQ.Web/Components/Pages/Error.razor`:

```razor
@page "/Error"
@using Microsoft.AspNetCore.Components.Web

<PageTitle>WealthIQ — Fehler</PageTitle>

<MudContainer Class="mt-8">
    <MudAlert Severity="Severity.Error" Variant="Variant.Filled">
        Ein unerwarteter Fehler ist aufgetreten.
    </MudAlert>
    <MudText Typo="Typo.body2" Class="mt-4">
        Bitte lade die Seite neu. Tritt der Fehler erneut auf, prüfe die Logs der Anwendung.
    </MudText>
    @if (!string.IsNullOrEmpty(RequestId))
    {
        <MudText Typo="Typo.caption" Class="mt-2">Request-ID: <code>@RequestId</code></MudText>
    }
    <MudButton Href="/" Variant="Variant.Filled" Color="Color.Primary" Class="mt-4">Zur Startseite</MudButton>
</MudContainer>

@code {
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    private string? RequestId { get; set; }

    protected override void OnInitialized()
        => RequestId = HttpContext?.TraceIdentifier;
}
```

> The `/Error` handler renders statically (errors break the circuit), so this page must not depend on interactive features. The `HttpContext` cascading parameter is available during static server rendering.

- [ ] **Step 2: Build and verify the route resolves**

Run: `dotnet build WealthIQ.slnx`
Expected: PASS (compiles). Confirm there are no duplicate `@page "/Error"` routes (`grep` shows only this file).

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Error.razor
git commit -m "fix(web): add /Error page for the production exception handler"
```

---

## Task 12: Light configuration fallback for data paths (#8)

**Files:**
- Modify: `src/WealthIQ.Web/Program.cs:21-27`
- Modify: `src/WealthIQ.Web/appsettings.json`

**Why:** Data/reference paths are derived purely from `ContentRootPath/../..`, fragile outside the source-tree layout. A light fix: read optional configuration keys (`DataPaths:Root`, `DataPaths:Reference`) and fall back to the current repo-relative default. Full deployment hardening (copying reference files as content, publish profiles) is out of v1 scope.

- [ ] **Step 1: Add config keys with the repo-relative default documented**

In `src/WealthIQ.Web/appsettings.json`, add a `DataPaths` section (create the file's object entries alongside existing keys):

```json
{
  "DataPaths": {
    "Root": "",
    "Reference": ""
  }
}
```

> Empty string means "use the repo-relative default". Document in the section that absolute paths override it.

- [ ] **Step 2: Honor the config in `Program.cs`**

In `Program.cs`, replace `:21-27`:

```csharp
// --- Local data layout ---
// ContentRootPath = src/WealthIQ.Web → repo root is two levels up.
var repoData = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));
var referenceDir = Path.Combine(repoData, "reference");
var appDataDir = Path.Combine(repoData, "app");
var auditDir = Path.Combine(appDataDir, "audit");
var dbPath = Path.Combine(appDataDir, "wealthiq.db");
Directory.CreateDirectory(auditDir);
```
with:
```csharp
// --- Local data layout ---
// Defaults are repo-relative (ContentRootPath = src/WealthIQ.Web → repo root is two levels up).
// Optional config overrides: DataPaths:Root (the data/ folder) and DataPaths:Reference.
var defaultRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));
var repoData = string.IsNullOrWhiteSpace(builder.Configuration["DataPaths:Root"])
    ? defaultRoot
    : Path.GetFullPath(builder.Configuration["DataPaths:Root"]!);
var referenceDir = string.IsNullOrWhiteSpace(builder.Configuration["DataPaths:Reference"])
    ? Path.Combine(repoData, "reference")
    : Path.GetFullPath(builder.Configuration["DataPaths:Reference"]!);
var appDataDir = Path.Combine(repoData, "app");
var auditDir = Path.Combine(appDataDir, "audit");
var dbPath = Path.Combine(appDataDir, "wealthiq.db");
Directory.CreateDirectory(auditDir);
```

- [ ] **Step 3: Build and verify default behavior is unchanged**

Run: `dotnet build WealthIQ.slnx && dotnet run --project src/WealthIQ.Web`
Expected: with empty config keys, the app uses the same repo-relative paths as before (DB at `data/app/wealthiq.db`, seeds from `data/reference`).

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Program.cs src/WealthIQ.Web/appsettings.json
git commit -m "fix(web): allow optional config override of data/reference paths"
```

---

## Task 13: Delete previous temp files before replacing the selection (#13)

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Import.razor:83-99`

**Why:** `OnFilesSelected` clears `_pendingFiles` (`:87`) before deleting the previous temp copies, so selecting a new file set leaks the prior `%TEMP%` files.

- [ ] **Step 1: Delete existing pending temp files before clearing**

In `Import.razor`, replace the start of `OnFilesSelected` (`:85-87`):

```csharp
        _error = null;
        _results.Clear();
        _pendingFiles.Clear();
```
with:
```csharp
        _error = null;
        _results.Clear();
        // Delete temp copies from a previous selection before replacing the list, or they leak in %TEMP%.
        foreach (var (_, previousPath) in _pendingFiles)
        {
            if (File.Exists(previousPath)) File.Delete(previousPath);
        }
        _pendingFiles.Clear();
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build WealthIQ.slnx`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Import.razor
git commit -m "fix(web): delete previous upload temp files when selection changes"
```

---

## Task 14: Show the real total in import progress (#14)

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Import.razor:33`, `74-81`, `123-124`

**Why:** `RunImport` clears `_pendingFiles` (`:124`) before the loop, but the button label divides by `_pendingFiles.Count` (`:33`), so it shows `Importiere… (0/0)` during processing.

- [ ] **Step 1: Add a `_totalCount` field**

In `Import.razor`'s `@code` block (near `:80`, with the other fields), add:

```csharp
    private int _totalCount;
```

- [ ] **Step 2: Set it before clearing and use it in the label**

In `RunImport`, replace `:123-124`:

```csharp
        var toProcess = _pendingFiles.ToList();
        _pendingFiles.Clear();
```
with:
```csharp
        var toProcess = _pendingFiles.ToList();
        _totalCount = toProcess.Count;
        _pendingFiles.Clear();
```

In the button label (`:33`), replace:

```razor
            @(_busy ? $"Importiere… ({_doneCount}/{_pendingFiles.Count})" : "Import starten")
```
with:
```razor
            @(_busy ? $"Importiere… ({_doneCount}/{_totalCount})" : "Import starten")
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build WealthIQ.slnx`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Import.razor
git commit -m "fix(web): show real file total in import progress label"
```

---

## Task 15: Apply the `isin` query parameter on parameter changes (#15)

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Audit.razor:88-103`

**Why:** `IsinQuery` is copied into `_filterIsin` only in `OnInitializedAsync`. If Blazor reuses the component across navigation (`/audit` → `/audit?isin=...`), the filter stays stale. It must react in `OnParametersSet`, but without clobbering a value the user typed into the text field.

- [ ] **Step 1: Move data loading out of init and track the last applied query**

In `Audit.razor`, add a field near `:79` (with the other fields):

```csharp
    private string? _appliedIsinQuery;
```

Replace `OnInitializedAsync` (`:88-103`) with an `OnInitializedAsync` that only loads data, plus an `OnParametersSet` that applies the query when it actually changes:

```csharp
    protected override async Task OnInitializedAsync()
    {
        try
        {
            _batches = await AuditStore.GetBatchesAsync();

            var ledger = await LedgerStore.LoadLedgerAsync();
            var instrumentById = ledger.Instruments.ToDictionary(i => i.InstrumentId);
            _entries = ledger.Entries.Select(e => ToView(e, instrumentById)).ToList();
        }
        catch (Exception ex)
        {
            _error = $"Audit-Daten konnten nicht geladen werden: {ex.Message}";
        }
    }

    protected override void OnParametersSet()
    {
        // Apply the query string only when it changes, so a user-typed filter is not clobbered on re-render.
        if (IsinQuery != _appliedIsinQuery)
        {
            _appliedIsinQuery = IsinQuery;
            _filterIsin = IsinQuery;
        }
    }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build WealthIQ.slnx`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Audit.razor
git commit -m "fix(web): apply audit isin query param on parameter changes"
```

---

## Task 16: Pin the vulnerable transitive `System.Security.Cryptography.Xml` (#vuln)

**Files:**
- Create: `Directory.Packages.props` (repo root) — or add a direct pinned reference
- Modify: `src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj` (only if not using central management)

**Why:** `dotnet test` emits high-severity NuGet warnings for `System.Security.Cryptography.Xml` 9.0.0, which is pulled in transitively (no direct reference exists). Pin a patched version to silence the advisory.

- [ ] **Step 1: Identify the patched version and the consuming project**

Run: `dotnet list src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj package --include-transitive --vulnerable`
Expected: shows `System.Security.Cryptography.Xml` 9.0.0 as transitive (likely via `Microsoft.EntityFrameworkCore.Design`). Note the recommended fixed version (a 9.0.x patch ≥ the advisory's fixed version).

- [ ] **Step 2: Add a direct pinned reference to override the transitive version**

In `src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj`, inside the existing `<ItemGroup>` with package references, add (substitute the actual patched version from Step 1):

```xml
    <!-- Pin transitive dependency to a patched version to clear NU1903 advisory (not used directly). -->
    <PackageReference Include="System.Security.Cryptography.Xml" Version="9.0.<patched>" />
```

- [ ] **Step 3: Restore and confirm the advisory is gone**

Run: `dotnet restore WealthIQ.slnx && dotnet build WealthIQ.slnx`
Expected: build succeeds with no NU1903/NU1902 warning for `System.Security.Cryptography.Xml`. Re-run `dotnet list ... --vulnerable` → no vulnerable packages.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test WealthIQ.slnx`
Expected: PASS, warning gone.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj
git commit -m "chore(deps): pin patched System.Security.Cryptography.Xml to clear advisory"
```

---

## Final verification

- [ ] **Step 1: Clean build (Release, as CI runs)**

Run: `dotnet clean WealthIQ.slnx && dotnet build WealthIQ.slnx --configuration Release`
Expected: PASS.

- [ ] **Step 2: Full test suite in Release (mirrors CI)**

Run: `dotnet test WealthIQ.slnx --configuration Release --no-build`
Expected: PASS — all pre-existing tests plus the new ones.

- [ ] **Step 3: Format check**

Run: `dotnet format WealthIQ.slnx --verify-no-changes`
Expected: no changes. If it reports diffs, run `dotnet format WealthIQ.slnx` and commit.

- [ ] **Step 4: Update CLAUDE.md known thin spots**

In `CLAUDE.md` under "Tax-pipeline guardrails / Known thin spots", note the resolved items and the remaining ones: transfer/adjustment entries now fail fast (semantics still unimplemented — no data); Vorabpauschale for a position held *beyond* the last ledger entry still needs an as-of/through-year parameter. Commit:

```bash
git add CLAUDE.md
git commit -m "docs: update tax thin-spots after code-review fixes"
```

- [ ] **Step 5: Manual smoke test**

Run: `dotnet run --project src/WealthIQ.Web`
Expected: app starts, migrates the two new migrations, seeds, and the Import/Audit/Steuerreport pages work; a successful import commits and a deliberately malformed import shows a Failed batch with diagnostics on the Audit page.

---

## Self-Review notes

- **Spec coverage:** every valid finding maps to a task (triage table); deferred items are explicitly listed with rationale.
- **Migrations:** two added (Task 2 unique index, Task 9 status column), applied at Web startup via the existing `db.Database.Migrate()`.
- **Type consistency:** `ImportBatch.Status` (`ImportBatchStatus`) ↔ `ImportBatchRow.Status` (string via `ToString()`) ↔ `ImportBatchView.Status` (string) ↔ Audit page `@context.Status`; `IImportStore.PersistFailedImportAsync` signature identical across interface, `SqliteImportStore`, and `FakeImportStore`; `OpenLot.OpenSourceReference` set in both `FiFoMatcher` and `GermanTaxCalculator.CreateLongLot` and read in `FiFoMatcher`'s ordering.
- **Test conflict recorded:** Task 3 deliberately replaces `Calculate_MissingYearEndPrice_SkipsVorabpauschale` (the only existing test this plan overrides), justified by the CLAUDE.md blocking-data guardrail.
