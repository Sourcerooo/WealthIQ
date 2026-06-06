# Trader's Place Import + Per-Account Tax Report — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import Trader's Place CSV statements (two complementary exports) into the canonical ledger, capture broker-withheld KESt, resolve ISIN-less dividends via an editable alias table, and render the German tax report per account.

**Architecture:** A new `TradersPlaceStatementImporter` (Infrastructure) ingests both CSVs in one import, classifying each file by header signature and routing by transaction type. KESt rides on `TradeEntry.WithheldTax` (JSON-persisted, excluded from FIFO gain math) and surfaces through `GermanTaxEntry.WithheldKESt`. The tax calculator (already account-scoped internally) tags every emitted entry with its `AccountId`; `AnnualTaxReportService` groups by `(AccountId, Year)` and the Steuerreport page gains an account dropdown.

**Tech Stack:** C# / .NET 10, EF Core + SQLite, Blazor Server + MudBlazor, xUnit. CI runs on ubuntu-latest — use `Encoding.Latin1` (built-in, ICU-independent) for the Windows-1252 CSVs; no extra NuGet packages.

**Spec:** `docs/superpowers/specs/2026-06-06-tradersplace-import-and-per-account-tax-report-design.md`

---

## Background the implementer must know

- **Ledger entries are serialized whole to `PortfolioEntryRow.PayloadJson`** (see `PortfolioEntryMapper`). Adding a property to `TradeEntry` therefore needs **no DB migration** — old rows deserialize the missing field to its default. Only the new dividend-alias *table* needs a migration.
- **The tax calculator is already account-scoped** (lots/dividends/Vorabpauschale filter by `AccountId` in `GermanTaxCalculator` and `FiFoMatcher`). The only place accounts get mixed is the **reporting** layer.
- **Currency enum** (`WealthIQ.Domain.Enumeration.Currency`) contains `EUR, USD, GBP, CHF, …`. All Trader's Place sample data is EUR.
- **Instrument enrichment**: the importer creates `Instrument`s with a stable id derived from ISIN and `Teilfreistellungsquote = 0`; `JsonInstrumentProfileEnricher` / `DbInstrumentProfileEnricher` later overwrite `Name/Type/Teilfreistellungsquote/SubjectToVorabpauschale` from the profile keyed by ISIN. So every Trader's Place ISIN **must** have a profile in `instruments.json`, or it's a blocking error at tax replay.
- **Stable instrument id** is `MD5(ISIN.ToUpperInvariant())` cast to a Guid (mirror `IbkrStatementImporter.CreateStableInstrumentId`). Using ISIN as the identity makes a dividend's related instrument unify with the trade lots of the same ISIN.

## File map

**Domain**
- Modify `src/WealthIQ.Domain/Model/Ledger/TradeEntry.cs` — add optional `Money WithheldTax`.
- Modify `src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs` — add `AccountId AccountId`, `decimal WithheldKESt`.

**Application**
- Create `src/WealthIQ.Application/ReferenceData/Interface/IDividendAliasMap.cs`.
- Create `src/WealthIQ.Application/ReferenceData/Interface/IDividendAliasStore.cs`.
- Create `src/WealthIQ.Application/ReferenceData/DividendAliasNormalizer.cs`.
- Create `src/WealthIQ.Application/ReferenceData/DividendAliasRefreshModels.cs` + `DividendAliasRefreshService.cs`.
- Modify `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs` — tag AccountId, allocate KESt.
- Modify `src/WealthIQ.Application/Tax/Report/TaxReportSummary.cs` — add `WithheldKESt`.
- Create `src/WealthIQ.Application/Tax/Report/AccountTaxReport.cs`.
- Modify `src/WealthIQ.Application/Tax/Report/AnnualTaxReportService.cs` — per-account grouping + KESt.
- Modify `src/WealthIQ.Application/Import/StatementImportPipeline.cs` — importer selection + directory ingest.
- Modify `src/WealthIQ.Application/Persistence/Interface/IRawFileStore.cs` — add `IngestDirectory`.

**Infrastructure**
- Create `src/WealthIQ.Infrastructure/TradersPlace/Import/TradersPlaceCsv.cs` (parsing helpers).
- Create `src/WealthIQ.Infrastructure/TradersPlace/Import/TradersPlaceStatementImporter.cs`.
- Create `src/WealthIQ.Infrastructure/Persistence/Rows/DividendAliasRow.cs`.
- Create `src/WealthIQ.Infrastructure/ReferenceData/DbDividendAliasMap.cs` + `DbDividendAliasStore.cs`.
- Modify `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs` — `DbSet<DividendAliasRow>` + key.
- Modify `src/WealthIQ.Infrastructure/ReferenceData/ReferenceDataSeeder.cs` — seed aliases.
- Modify `src/WealthIQ.Application/ReferenceData/ReferenceDataSources.cs` — add alias csv path. (find exact path in Task 11)
- Modify `src/WealthIQ.Infrastructure/Ingest/FileSystemRawFileStore.cs` — implement `IngestDirectory`.
- New EF migration under `src/WealthIQ.Infrastructure/Persistence/Migrations/`.

**Web**
- Modify `src/WealthIQ.Web/Program.cs` — DI for importer(s), alias map/store/service.
- Modify `src/WealthIQ.Web/Components/Pages/Import.razor` — broker selector + two-file flow.
- Modify `src/WealthIQ.Web/Components/Pages/Steuerreport.razor` — account dropdown + KESt display.
- Modify `src/WealthIQ.Web/Composition/DeterministicAccount.cs` usage (Trader's Place key) — no file change, just new call.
- Add a dividend-alias panel to `src/WealthIQ.Web/Components/Pages/DataAdmin.razor` (Stammdaten).

**Reference & test data**
- Modify `data/reference/instruments.json` — add the 4 missing Trader's Place ISINs.
- Create `data/reference/tradersplace_dividend_aliases.csv`.
- Create `data/test/tradersplace/` golden fixtures (the two CSVs + config).

---

## Phase A — Domain & tax-engine changes

### Task 1: Add `WithheldTax` to `TradeEntry`

**Files:**
- Modify: `src/WealthIQ.Domain/Model/Ledger/TradeEntry.cs`
- Test: `tests/WealthIQ.Tests/Domain/TradeEntryTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Domain/TradeEntryTests.cs`:

```csharp
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using Xunit;

namespace WealthIQ.Tests.Domain;

public sealed class TradeEntryTests
{
    private static SourceProvenance Prov() => new()
    {
        SourceSystem = "TEST",
        ImportFormat = "TEST",
        SourceLocation = "test",
        SourceRecordReference = "ref-1"
    };

    [Fact]
    public void Constructor_WithoutWithheldTax_DefaultsToZeroEur()
    {
        var entry = new TradeEntry(
            PortfolioEntryId.NewId(), AccountId.NewId(),
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateOnly(2025, 1, 1), Prov(), InstrumentId.NewId(),
            TradeSide.Sell, new Quantity(10m), new Money(100m, Currency.EUR),
            new Money(0m, Currency.EUR), new Money(0m, Currency.EUR));

        Assert.Equal(0m, entry.WithheldTax.Amount);
    }

    [Fact]
    public void Constructor_WithWithheldTax_StoresIt()
    {
        var entry = new TradeEntry(
            PortfolioEntryId.NewId(), AccountId.NewId(),
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateOnly(2025, 1, 1), Prov(), InstrumentId.NewId(),
            TradeSide.Sell, new Quantity(10m), new Money(100m, Currency.EUR),
            new Money(0m, Currency.EUR), new Money(0m, Currency.EUR),
            new Money(340.29m, Currency.EUR));

        Assert.Equal(340.29m, entry.WithheldTax.Amount);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TradeEntryTests"`
Expected: FAIL — `TradeEntry` has no constructor accepting an 11th `Money` and no `WithheldTax` member.

- [ ] **Step 3: Add the optional parameter and property**

In `src/WealthIQ.Domain/Model/Ledger/TradeEntry.cs`, add a parameter after `Money taxes` and assign it. The new parameter is optional (`default` = `Money(0, Currency.USD)`), so normalize null/default to EUR-zero. Replace the constructor signature and body tail:

```csharp
    public TradeEntry(
        PortfolioEntryId entryId,
        AccountId accountId,
        DateTimeOffset occurredAt,
        DateOnly effectiveDate,
        SourceProvenance sourceProvenance,
        InstrumentId instrumentId,
        TradeSide side,
        Quantity quantity,
        Money unitPrice,
        Money fees,
        Money taxes,
        Money withheldTax = default)
        : base(entryId, accountId, occurredAt, effectiveDate, PortfolioEntryCategory.Trade, sourceProvenance)
    {
        if (quantity.Value <= 0m)
        {
            throw new InvalidOperationException("Trade quantity must be greater than zero.");
        }

        if (unitPrice.Amount <= 0m)
        {
            throw new InvalidOperationException("Trade unit price must be greater than zero.");
        }

        EnsureNonNegative(fees, nameof(fees));
        EnsureNonNegative(taxes, nameof(taxes));
        EnsureNonNegative(withheldTax, nameof(withheldTax));

        InstrumentId = instrumentId;
        Side = side;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Fees = fees;
        Taxes = taxes;
        WithheldTax = withheldTax;
    }
```

Add the property next to `Taxes`:

```csharp
    public Money Taxes { get; }

    /// <summary>Capital-gains tax already withheld by the broker at sale (e.g. German KESt). Display/
    /// reconciliation only — NEVER part of FIFO proceeds/cost math. Default zero EUR.</summary>
    public Money WithheldTax { get; }
```

> Note: `default(Money)` is `Money(0m, Currency.USD)` (USD is enum value 0). The amount is 0 either way and `EnsureNonNegative` passes; `WithheldTax` is only read for its amount, so the currency of a zero is irrelevant. Callers that set a real KESt always pass an explicit EUR `Money`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TradeEntryTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Domain/Model/Ledger/TradeEntry.cs tests/WealthIQ.Tests/Domain/TradeEntryTests.cs
git commit -m "feat(domain): add optional WithheldTax to TradeEntry for broker-withheld KESt"
```

---

### Task 2: Add `AccountId` and `WithheldKESt` to `GermanTaxEntry`

**Files:**
- Modify: `src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs`
- Test: `tests/WealthIQ.Tests/Domain/GermanTaxEntryTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Domain/GermanTaxEntryTests.cs`:

```csharp
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Tax;
using Xunit;

namespace WealthIQ.Tests.Domain;

public sealed class GermanTaxEntryTests
{
    [Fact]
    public void NewEntry_DefaultsAccountAndKestToZero()
    {
        var entry = new GermanTaxEntry(2025, new DateOnly(2025, 1, 1),
            GermanTaxEntryType.Sell, "AAA", "DE0001", 100m, 70m);

        Assert.Equal(default(AccountId), entry.AccountId);
        Assert.Equal(0m, entry.WithheldKESt);
    }

    [Fact]
    public void NewEntry_CanCarryAccountAndKest()
    {
        var account = AccountId.NewId();
        var entry = new GermanTaxEntry(2025, new DateOnly(2025, 1, 1),
            GermanTaxEntryType.Sell, "AAA", "DE0001", 100m, 70m,
            AccountId: account, WithheldKESt: 12.34m);

        Assert.Equal(account, entry.AccountId);
        Assert.Equal(12.34m, entry.WithheldKESt);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxEntryTests"`
Expected: FAIL — no `AccountId`/`WithheldKESt` members.

- [ ] **Step 3: Add the two optional positional parameters**

In `src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs`, add `using WealthIQ.Domain.Model.General;` and append two parameters at the very end of the record-struct parameter list (after `decimal MonthFactor = 0m`):

```csharp
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Tax;

public readonly record struct GermanTaxEntry(
    int Year,
    DateOnly Date,
    GermanTaxEntryType Type,
    string Symbol,
    string Isin,
    decimal RawAmount,
    decimal TaxableAmount,
    decimal UsedVorabpauschale = 0m,
    decimal ForeignWithholdingTax = 0m,
    decimal QuantitySold = 0m,
    decimal SaleProceeds = 0m,
    decimal AcquisitionCosts = 0m,
    DateOnly OpenedOn = default,
    decimal Fees = 0m,
    string Origin = "",
    string SourceReference = "",
    string CloseReference = "",
    string SourceFile = "",
    decimal OriginalAmount = 0m,
    string OriginalCurrency = "",
    decimal YearStartPrice = 0m,
    decimal YearEndPrice = 0m,
    decimal BasisRate = 0m,
    decimal HeldQuantity = 0m,
    decimal DistributionPerShare = 0m,
    decimal MonthFactor = 0m,
    // --- Per-account reporting + broker-withheld German KESt (display/aggregation only) ---
    AccountId AccountId = default,
    decimal WithheldKESt = 0m);
```

Keep the existing explanatory comments on the prior fields.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxEntryTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs tests/WealthIQ.Tests/Domain/GermanTaxEntryTests.cs
git commit -m "feat(domain): add AccountId and WithheldKESt to GermanTaxEntry"
```

---

### Task 3: Tag entries with AccountId + allocate KESt in `GermanTaxCalculator`

**Files:**
- Modify: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs`
- Test: `tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorKestAndAccountTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorKestAndAccountTests.cs`:

```csharp
using WealthIQ.Application.Tax;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using Xunit;

namespace WealthIQ.Tests.Application.Tax;

public sealed class GermanTaxCalculatorKestAndAccountTests
{
    [Fact]
    public void Calculate_SellWithKest_AllocatesKestAndTagsAccount_ExcludedFromGain()
    {
        var account = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "DE0001", "AAA", "Alpha", 0m)
        {
            SubjectToVorabpauschale = false
        };

        // Buy 10 @ 100 (2024-01-10) within one year, sell 10 @ 120 (2024-06-10), KESt 5.00.
        var buy = TaxEntries.Trade(account, instrumentId, TradeSide.Buy, 10m, 100m,
            new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero), "BUY-1");
        var sell = new TradeEntry(
            PortfolioEntryId.NewId(), account,
            new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 6, 10), TaxEntries.Provenance("SELL-1"),
            instrumentId, TradeSide.Sell, new Quantity(10m),
            new Money(120m, Currency.EUR), new Money(0m, Currency.EUR),
            new Money(0m, Currency.EUR), new Money(5m, Currency.EUR));

        var ledger = new PortfolioLedger(new PortfolioEntry[] { buy, sell }, new[] { instrument });

        var calculator = new GermanTaxCalculator(
            new FakeBasisInterestRateProvider((2024, 0m)),
            new FakeYearEndPriceProvider(),
            new FakeFxRateLookup());

        var result = calculator.Calculate(ledger, new[] { instrument });

        var sellEntry = Assert.Single(result.Entries, e => e.Type == GermanTaxEntryType.Sell);
        Assert.Equal(200m, sellEntry.RawAmount);     // gain unaffected by KESt
        Assert.Equal(5m, sellEntry.WithheldKESt);    // KESt captured
        Assert.Equal(account, sellEntry.AccountId);  // tagged with account
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxCalculatorKestAndAccountTests"`
Expected: FAIL — `WithheldKESt`/`AccountId` are 0/default because the calculator does not yet populate them.

- [ ] **Step 3: Populate AccountId on every emitted entry and allocate KESt on sells**

In `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs`:

In `ProcessTrade`, inside the `foreach (var consumption in matchResult.Consumptions)` loop, compute the KESt slice and add it to the `GermanTaxEntry`. Find the `ledger.Add(new GermanTaxEntry(...))` for the `Sell` and replace it with:

```csharp
            var kestSlice = tradeEntry.WithheldTax.Amount <= 0m
                ? 0m
                : tradeEntry.WithheldTax.Amount * (consumption.MatchedQuantity.Value / tradeEntry.Quantity.Value);

            ledger.Add(new GermanTaxEntry(
                tradeEntry.OccurredAt.Year,
                DateOnly.FromDateTime(tradeEntry.OccurredAt.UtcDateTime),
                GermanTaxEntryType.Sell,
                instrument.Symbol,
                instrument.ISIN,
                rawProfit,
                taxableProfit,
                usedVorabpauschale,
                QuantitySold: consumption.MatchedQuantity.Value,
                SaleProceeds: saleProceeds.Amount,
                AcquisitionCosts: acquisitionCosts.Amount,
                OpenedOn: consumption.OpenTradeDate,
                Fees: feesEur,
                SourceReference: originalLot.OpenSourceReference,
                CloseReference: tradeEntry.SourceProvenance.SourceRecordReference,
                SourceFile: tradeEntry.SourceProvenance.SourceLocation,
                AccountId: tradeEntry.AccountId,
                WithheldKESt: kestSlice));
```

In `ProcessCash`, add `AccountId: cashEntry.AccountId` to each of the three `new GermanTaxEntry(...)` calls (Dividend, Interest, WithholdingTax) — append it as a named argument before the closing `)` of each.

In `PerformYearEndClosing`, add `AccountId: lot.AccountId` as a named argument to the Vorabpauschale `new GermanTaxEntry(...)`.

> KESt is intentionally **not** referenced in `ConvertProceedsToEur`/`ConvertCostBasisToEur` — leave those methods unchanged so the gain math ignores it.

- [ ] **Step 4: Run the touched suites to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxCalculator"`
Expected: PASS — the new KEST/account test plus all existing calculator tests (they pass `AccountId`/`WithheldKESt` as defaults, unaffected).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/Tax/GermanTaxCalculator.cs tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorKestAndAccountTests.cs
git commit -m "feat(tax): tag tax entries with AccountId and allocate withheld KESt across FIFO slices"
```

---

### Task 4: Per-account report shape + KESt in `AnnualTaxReportService`

**Files:**
- Modify: `src/WealthIQ.Application/Tax/Report/TaxReportSummary.cs`
- Create: `src/WealthIQ.Application/Tax/Report/AccountTaxReport.cs`
- Modify: `src/WealthIQ.Application/Tax/Report/AnnualTaxReportService.cs`
- Modify: `tests/WealthIQ.Tests/Application/Tax/AnnualTaxReportServiceTests.cs`
- Test: add a new per-account test in the same file.

- [ ] **Step 1: Write the failing test**

Replace the body of the existing `Generate_BuySellAndDividendSameYear_ProducesYearSummaryAndSections` assertions to read through the new shape, and add a second test. Edit `tests/WealthIQ.Tests/Application/Tax/AnnualTaxReportServiceTests.cs` — change the assertion block after `var reports = await service.GenerateAsync();` to:

```csharp
        var accountReport = Assert.Single(reports);
        Assert.Equal(accountId.Value, accountReport.AccountId);
        Assert.Equal("U1", accountReport.AccountNumber);

        var report = Assert.Single(accountReport.Years);
        Assert.Equal(2024, report.Year);
        Assert.Single(report.Sells);
        Assert.Single(report.Dividends);
        Assert.Empty(report.Vorabpauschale);

        Assert.Equal(200m, report.Summary.NetRealizedGainsTaxable);
        Assert.Equal(50m, report.Summary.DividendsTaxable);
        Assert.Equal(0m, report.Summary.InterestTaxable);
        Assert.Equal(0m, report.Summary.VorabpauschaleTaxable);
        Assert.Equal(0m, report.Summary.ForeignWithholdingTax);
        Assert.Equal(0m, report.Summary.WithheldKESt);
        // (200 + 50) * 0.26375 = 65.9375
        Assert.Equal(65.9375m, report.Summary.EstimatedTax);
```

Add this second test method to the class:

```csharp
    [Fact]
    public async Task Generate_TwoAccounts_ReportsAreSeparated()
    {
        var accountA = AccountId.NewId();
        var accountB = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "DE0001", "AAA", "Alpha", 0m)
        {
            SubjectToVorabpauschale = false
        };

        var buyA = TaxEntries.Trade(accountA, instrumentId, TradeSide.Buy, 10m, 100m,
            new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero), "A-BUY");
        var sellA = TaxEntries.Trade(accountA, instrumentId, TradeSide.Sell, 10m, 120m,
            new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero), "A-SELL");
        var buyB = TaxEntries.Trade(accountB, instrumentId, TradeSide.Buy, 5m, 100m,
            new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero), "B-BUY");
        var sellB = TaxEntries.Trade(accountB, instrumentId, TradeSide.Sell, 5m, 110m,
            new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero), "B-SELL");

        var ledger = new PortfolioLedger(
            new PortfolioEntry[] { buyA, sellA, buyB, sellB },
            new[] { instrument },
            new[] { new Account(accountA, "AAA-1"), new Account(accountB, "BBB-2") });

        var service = new AnnualTaxReportService(
            new FixedLedgerStore(ledger),
            new InstrumentCatalogBuilder(new IdentityProfileEnricher()),
            new GermanTaxCalculator(
                new FakeBasisInterestRateProvider((2024, 0m)),
                new FakeYearEndPriceProvider(),
                new FakeFxRateLookup()));

        var reports = await service.GenerateAsync();

        Assert.Equal(2, reports.Count);
        var a = Assert.Single(reports, r => r.AccountNumber == "AAA-1");
        var b = Assert.Single(reports, r => r.AccountNumber == "BBB-2");
        Assert.Equal(200m, a.Years.Single().Summary.NetRealizedGainsTaxable);  // 10 * (120-100)
        Assert.Equal(50m, b.Years.Single().Summary.NetRealizedGainsTaxable);   // 5 * (110-100)
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~AnnualTaxReportServiceTests"`
Expected: FAIL — `reports` is still `IReadOnlyList<AnnualTaxReport>` with no `AccountId/AccountNumber/Years`, and `Summary.WithheldKESt` doesn't exist.

- [ ] **Step 3a: Add `WithheldKESt` to `TaxReportSummary`**

Replace `src/WealthIQ.Application/Tax/Report/TaxReportSummary.cs` record with:

```csharp
public sealed record TaxReportSummary(
    decimal NetRealizedGainsTaxable,
    decimal DividendsTaxable,
    decimal InterestTaxable,
    decimal VorabpauschaleTaxable,
    decimal ForeignWithholdingTax,
    decimal EstimatedTax,
    decimal WithheldKESt = 0m);
```

(Keep the existing XML doc comment above it.)

- [ ] **Step 3b: Create `AccountTaxReport`**

Create `src/WealthIQ.Application/Tax/Report/AccountTaxReport.cs`:

```csharp
namespace WealthIQ.Application.Tax.Report;

/// <summary>All tax years for a single account. The report is strictly per account so data from
/// different brokers/accounts is never mixed (spec §8).</summary>
public sealed record AccountTaxReport(
    Guid AccountId,
    string AccountNumber,
    IReadOnlyList<AnnualTaxReport> Years);
```

- [ ] **Step 3c: Rewrite `AnnualTaxReportService` to group per account**

Replace `src/WealthIQ.Application/Tax/Report/AnnualTaxReportService.cs` with:

```csharp
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Application.Tax.Report;

/// <summary>
/// Builds the yearly German tax report (spec §6, §9), grouped per account (spec §8). Loads the
/// persisted ledger, enriches the instrument catalog, runs <see cref="GermanTaxCalculator"/>, then
/// groups by (AccountId, Year). A missing FX/reference value surfaces as the calculator's exception.
/// </summary>
public sealed class AnnualTaxReportService(
    ILedgerStore ledgerStore,
    InstrumentCatalogBuilder catalogBuilder,
    GermanTaxCalculator calculator)
{
    private const decimal AbgeltungsteuerWithSoli = 0.26375m; // 25 % + 5.5 % Soli

    public async Task<IReadOnlyList<AccountTaxReport>> GenerateAsync(CancellationToken ct = default)
    {
        var ledger = await ledgerStore.LoadLedgerAsync(ct);
        var catalog = catalogBuilder.Build(ledger.Instruments);
        var result = calculator.Calculate(ledger, catalog);

        var accountNumbers = ledger.Accounts.ToDictionary(a => a.AccountId, a => a.AccountNumber);

        return result.Entries
            .GroupBy(e => e.AccountId)
            .OrderBy(g => accountNumbers.TryGetValue(g.Key, out var n) ? n : g.Key.ToString(), StringComparer.Ordinal)
            .Select(accountGroup => new AccountTaxReport(
                accountGroup.Key.Value,
                accountNumbers.TryGetValue(accountGroup.Key, out var number) ? number : accountGroup.Key.ToString(),
                accountGroup
                    .GroupBy(e => e.Year)
                    .OrderBy(y => y.Key)
                    .Select(BuildAnnualReport)
                    .ToList()))
            .ToList();
    }

    private static AnnualTaxReport BuildAnnualReport(IGrouping<int, GermanTaxEntry> yearEntries)
    {
        var sells = yearEntries.Where(e => e.Type == GermanTaxEntryType.Sell).ToList();
        var dividends = yearEntries.Where(e => e.Type == GermanTaxEntryType.Dividend).ToList();
        var interest = yearEntries.Where(e => e.Type == GermanTaxEntryType.Interest).ToList();
        var withholding = yearEntries.Where(e => e.Type == GermanTaxEntryType.WithholdingTax).ToList();
        var vorab = yearEntries.Where(e => e.Type == GermanTaxEntryType.Vorabpauschale).ToList();

        var netSells = sells.Sum(e => e.TaxableAmount);
        var dividendTaxable = dividends.Sum(e => e.TaxableAmount);
        var interestTaxable = interest.Sum(e => e.TaxableAmount);
        var vorabTaxable = vorab.Sum(e => e.TaxableAmount);
        var foreignWithholding = withholding.Sum(e => e.ForeignWithholdingTax);
        var withheldKest = sells.Sum(e => e.WithheldKESt);

        var taxableBase = netSells + dividendTaxable + interestTaxable + vorabTaxable;
        var grossTax = Math.Max(0m, taxableBase) * AbgeltungsteuerWithSoli;
        var estimatedTax = Math.Max(0m, grossTax - foreignWithholding);

        var summary = new TaxReportSummary(
            netSells, dividendTaxable, interestTaxable, vorabTaxable, foreignWithholding, estimatedTax, withheldKest);
        return new AnnualTaxReport(yearEntries.Key, summary, sells, dividends, interest, withholding, vorab);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~AnnualTaxReportServiceTests"`
Expected: PASS (2 tests). The Web project will not compile yet (it consumes the old shape) — that's fixed in Task 17. Run only the filtered test here.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/Tax/Report/ tests/WealthIQ.Tests/Application/Tax/AnnualTaxReportServiceTests.cs
git commit -m "feat(tax): per-account tax report grouping and withheld-KESt summary"
```

---

## Phase B — Dividend alias map (reference data)

### Task 5: `IDividendAliasMap` + normalizer

**Files:**
- Create: `src/WealthIQ.Application/ReferenceData/Interface/IDividendAliasMap.cs`
- Create: `src/WealthIQ.Application/ReferenceData/DividendAliasNormalizer.cs`
- Test: `tests/WealthIQ.Tests/Application/ReferenceData/DividendAliasNormalizerTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Application/ReferenceData/DividendAliasNormalizerTests.cs`:

```csharp
using WealthIQ.Application.ReferenceData;
using Xunit;

namespace WealthIQ.Tests.Application.ReferenceData;

public sealed class DividendAliasNormalizerTests
{
    [Theory]
    [InlineData("VANGUARD S+P 500U.ETF DLD", "VANGUARD S+P 500U.ETF DLD")]
    [InlineData("  vanguard   s+p 500u.etf dld ", "VANGUARD S+P 500U.ETF DLD")]
    [InlineData("ISHSIV-DL T.BD20+YR DL  D", "ISHSIV-DL T.BD20+YR DL D")]
    public void Normalize_CollapsesWhitespaceAndUppercases(string input, string expected)
        => Assert.Equal(expected, DividendAliasNormalizer.Normalize(input));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DividendAliasNormalizerTests"`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Create interface and normalizer**

Create `src/WealthIQ.Application/ReferenceData/Interface/IDividendAliasMap.cs`:

```csharp
namespace WealthIQ.Application.ReferenceData.Interface;

/// <summary>Resolves the mangled dividend names in Trader's Place statements (which carry no ISIN)
/// to a canonical ISIN. Explicit, user-maintained mapping — no fuzzy matching (spec §6).</summary>
public interface IDividendAliasMap
{
    /// <summary>Returns the ISIN for an alias, or <c>null</c> if unmapped (caller must fail loud).</summary>
    string? ResolveIsin(string alias);
}
```

Create `src/WealthIQ.Application/ReferenceData/DividendAliasNormalizer.cs`:

```csharp
using System.Text.RegularExpressions;

namespace WealthIQ.Application.ReferenceData;

/// <summary>Canonicalizes dividend alias strings so trivial whitespace/case differences still match.</summary>
public static partial class DividendAliasNormalizer
{
    public static string Normalize(string alias)
        => WhitespaceRegex().Replace((alias ?? string.Empty).Trim(), " ").ToUpperInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DividendAliasNormalizerTests"`
Expected: PASS (3 cases).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/ReferenceData/Interface/IDividendAliasMap.cs src/WealthIQ.Application/ReferenceData/DividendAliasNormalizer.cs tests/WealthIQ.Tests/Application/ReferenceData/DividendAliasNormalizerTests.cs
git commit -m "feat(refdata): dividend alias map port + alias normalizer"
```

---

### Task 6: `DividendAliasRow` + DbContext + migration

**Files:**
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/DividendAliasRow.cs`
- Modify: `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs`
- Migration: generated under `src/WealthIQ.Infrastructure/Persistence/Migrations/`

- [ ] **Step 1: Create the row type**

Create `src/WealthIQ.Infrastructure/Persistence/Rows/DividendAliasRow.cs`:

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

/// <summary>A user-maintained Trader's Place dividend alias → ISIN mapping. Keyed by the normalized
/// alias so lookups are stable across whitespace/case variations.</summary>
public sealed class DividendAliasRow
{
    public string NormalizedAlias { get; set; } = "";
    public string Alias { get; set; } = "";   // original, for display in the UI
    public string Isin { get; set; } = "";
}
```

- [ ] **Step 2: Register the DbSet + key**

In `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs`, add the DbSet after `DataRefreshLog`:

```csharp
    public DbSet<DividendAliasRow> DividendAliases => Set<DividendAliasRow>();
```

And add the entity config at the end of `OnModelCreating`:

```csharp
        modelBuilder.Entity<DividendAliasRow>(e => e.HasKey(x => x.NormalizedAlias));
```

- [ ] **Step 3: Add the migration**

Run:

```bash
dotnet ef migrations add DividendAliases --project src/WealthIQ.Infrastructure
```

Expected: a new `*_DividendAliases.cs` migration creating a `DividendAliases` table with PK `NormalizedAlias`. Open it and confirm it only creates the new table (no other schema drift).

- [ ] **Step 4: Build to verify the model + migration compile**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Infrastructure/Persistence/
git commit -m "feat(persistence): DividendAliases table + migration"
```

---

### Task 7: `DbDividendAliasMap` + `DbDividendAliasStore`

**Files:**
- Create: `src/WealthIQ.Application/ReferenceData/Interface/IDividendAliasStore.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbDividendAliasMap.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbDividendAliasStore.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/DbDividendAliasMapTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Infrastructure/DbDividendAliasMapTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using Xunit;

namespace WealthIQ.Tests.Infrastructure;

public sealed class DbDividendAliasMapTests
{
    private static WealthIqDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new WealthIqDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public void ResolveIsin_NormalizesAndResolves_OrReturnsNullWhenUnmapped()
    {
        using var db = NewInMemoryDb();
        db.DividendAliases.Add(new DividendAliasRow
        {
            NormalizedAlias = "VANGUARD S+P 500U.ETF DLD",
            Alias = "VANGUARD S+P 500U.ETF DLD",
            Isin = "IE00B3XXRP09"
        });
        db.SaveChanges();

        var map = new DbDividendAliasMap(db);

        Assert.Equal("IE00B3XXRP09", map.ResolveIsin("  vanguard  s+p 500u.etf dld "));
        Assert.Null(map.ResolveIsin("UNKNOWN NAME"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DbDividendAliasMapTests"`
Expected: FAIL — `DbDividendAliasMap` does not exist.

- [ ] **Step 3: Create the store interface and both implementations**

Create `src/WealthIQ.Application/ReferenceData/Interface/IDividendAliasStore.cs`:

```csharp
namespace WealthIQ.Application.ReferenceData.Interface;

/// <summary>CRUD for the dividend alias → ISIN mapping (Stammdaten UI editing).</summary>
public interface IDividendAliasStore
{
    void Upsert(string alias, string isin);
    void Delete(string normalizedAlias);
    Task SaveChangesAsync(CancellationToken ct);
}
```

Create `src/WealthIQ.Infrastructure/ReferenceData/DbDividendAliasMap.cs`:

```csharp
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Dividend alias → ISIN lookup from the seeded <c>DividendAliases</c> table.
/// Loaded once on construction (mirrors <c>DbBasisInterestRateProvider</c>).</summary>
public sealed class DbDividendAliasMap : IDividendAliasMap
{
    private readonly Dictionary<string, string> _byNormalizedAlias;

    public DbDividendAliasMap(WealthIqDbContext db)
    {
        _byNormalizedAlias = db.DividendAliases.ToDictionary(x => x.NormalizedAlias, x => x.Isin);
    }

    public string? ResolveIsin(string alias)
        => _byNormalizedAlias.TryGetValue(DividendAliasNormalizer.Normalize(alias), out var isin) ? isin : null;
}
```

Create `src/WealthIQ.Infrastructure/ReferenceData/DbDividendAliasStore.cs`:

```csharp
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Upserts/deletes dividend aliases by normalized key.</summary>
public sealed class DbDividendAliasStore(WealthIqDbContext db) : IDividendAliasStore
{
    public void Upsert(string alias, string isin)
    {
        var normalized = DividendAliasNormalizer.Normalize(alias);
        var existing = db.DividendAliases.Find(normalized);
        if (existing is null)
        {
            db.DividendAliases.Add(new DividendAliasRow
            {
                NormalizedAlias = normalized,
                Alias = alias.Trim(),
                Isin = isin.Trim()
            });
        }
        else
        {
            existing.Alias = alias.Trim();
            existing.Isin = isin.Trim();
        }
    }

    public void Delete(string normalizedAlias)
    {
        var existing = db.DividendAliases.Find(normalizedAlias);
        if (existing is not null)
        {
            db.DividendAliases.Remove(existing);
        }
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DbDividendAliasMapTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/ReferenceData/Interface/IDividendAliasStore.cs src/WealthIQ.Infrastructure/ReferenceData/DbDividendAliasMap.cs src/WealthIQ.Infrastructure/ReferenceData/DbDividendAliasStore.cs tests/WealthIQ.Tests/Infrastructure/DbDividendAliasMapTests.cs
git commit -m "feat(refdata): DB-backed dividend alias map + store"
```

---

### Task 8: Seed file + seeder wiring

**Files:**
- Create: `data/reference/tradersplace_dividend_aliases.csv`
- Modify: `src/WealthIQ.Application/ReferenceData/ReferenceDataSources.cs`
- Modify: `src/WealthIQ.Infrastructure/ReferenceData/ReferenceDataSeeder.cs`
- Modify: `src/WealthIQ.Web/Program.cs` (the `ReferenceDataSources` construction)

- [ ] **Step 1: Create the seed CSV**

Create `data/reference/tradersplace_dividend_aliases.csv` (UTF-8, comma-separated, header line):

```csv
alias,isin
VANGUARD S+P 500U.ETF DLD,IE00B3XXRP09
ISHSIV-DL T.BD20+YR DL D,IE00BSKRJZ44
```

- [ ] **Step 2: Add the path to `ReferenceDataSources`**

Open `src/WealthIQ.Application/ReferenceData/ReferenceDataSources.cs` and add a property/parameter `DividendAliasCsvPath`. It is a record; add the new parameter at the end. Example (match the existing record's exact style):

```csharp
public sealed record ReferenceDataSources(
    string BasisInterestRateCsvPath,
    string HistoricalPriceCsvPath,
    string InstrumentProfileJsonPath,
    string InstrumentListingJsonPath,
    string FxRateCsvPath,
    string DividendAliasCsvPath);
```

> If the file already declares these as positional record parameters, just append `DividendAliasCsvPath`. If they are init properties, add a matching init property instead. Open the file and follow its actual shape.

- [ ] **Step 3: Construct it in Program.cs**

In `src/WealthIQ.Web/Program.cs`, extend the `new ReferenceDataSources(...)` call (around line 42) to pass the new path:

```csharp
var referenceDataSources = new ReferenceDataSources(
    Path.Combine(referenceDir, "basiszins.csv"),
    Path.Combine(referenceDir, "historical_prices.csv"),
    Path.Combine(referenceDir, "instruments.json"),
    Path.Combine(referenceDir, "listings.json"),
    Path.Combine(referenceDir, "fx_rates.csv"),
    Path.Combine(referenceDir, "tradersplace_dividend_aliases.csv"));
```

- [ ] **Step 4: Seed aliases when empty**

In `src/WealthIQ.Infrastructure/ReferenceData/ReferenceDataSeeder.cs`:

Add a seeding block inside `SeedIfEmptyAsync` (after the FxRates block, before `SaveChangesAsync`):

```csharp
        if (!await db.DividendAliases.AnyAsync(ct))
        {
            db.DividendAliases.AddRange(ReadDividendAliases(sources.DividendAliasCsvPath));
        }
```

Add the reader method (uses the existing `ReadCsv` helper and `DividendAliasNormalizer`):

```csharp
    private static IEnumerable<DividendAliasRow> ReadDividendAliases(string path)
    {
        foreach (var (_, parts) in ReadCsv(path, "Dividend alias file not found.", minColumns: 2))
        {
            var alias = parts[0].Trim();
            var isin = parts[1].Trim();
            if (alias.Length == 0 || isin.Length == 0)
            {
                continue;
            }

            yield return new DividendAliasRow
            {
                NormalizedAlias = WealthIQ.Application.ReferenceData.DividendAliasNormalizer.Normalize(alias),
                Alias = alias,
                Isin = isin
            };
        }
    }
```

> The alias values contain no commas, so the existing comma-split `ReadCsv` is safe here. If a future alias contains a comma, switch this one file to a semicolon separator and split accordingly.

- [ ] **Step 5: Build, then commit**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeds.

```bash
git add data/reference/tradersplace_dividend_aliases.csv src/WealthIQ.Application/ReferenceData/ReferenceDataSources.cs src/WealthIQ.Infrastructure/ReferenceData/ReferenceDataSeeder.cs src/WealthIQ.Web/Program.cs
git commit -m "feat(refdata): seed Trader's Place dividend aliases from committed CSV"
```

---

### Task 9: `DividendAliasRefreshService` (UI CRUD)

**Files:**
- Create: `src/WealthIQ.Application/ReferenceData/DividendAliasRefreshModels.cs`
- Create: `src/WealthIQ.Application/ReferenceData/DividendAliasRefreshService.cs`
- Test: `tests/WealthIQ.Tests/Application/ReferenceData/DividendAliasRefreshServiceTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Application/ReferenceData/DividendAliasRefreshServiceTests.cs`:

```csharp
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using Xunit;

namespace WealthIQ.Tests.Application.ReferenceData;

public sealed class DividendAliasRefreshServiceTests
{
    private sealed class FakeStore : IDividendAliasStore
    {
        public readonly List<(string Alias, string Isin)> Upserts = new();
        public readonly List<string> Deletes = new();
        public int Saves;
        public void Upsert(string alias, string isin) => Upserts.Add((alias, isin));
        public void Delete(string normalizedAlias) => Deletes.Add(normalizedAlias);
        public Task SaveChangesAsync(CancellationToken ct) { Saves++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task SetAsync_RejectsBlankAliasOrIsin()
    {
        var store = new FakeStore();
        var service = new DividendAliasRefreshService(store);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(" ", "IE00B3XXRP09"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("ALIAS", " "));
        Assert.Empty(store.Upserts);
    }

    [Fact]
    public async Task SetAsync_UpsertsAndSaves()
    {
        var store = new FakeStore();
        var service = new DividendAliasRefreshService(store);

        await service.SetAsync("VANGUARD S+P 500U.ETF DLD", "IE00B3XXRP09");

        Assert.Single(store.Upserts);
        Assert.Equal(1, store.Saves);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DividendAliasRefreshServiceTests"`
Expected: FAIL — service does not exist.

- [ ] **Step 3: Create the models + service**

Create `src/WealthIQ.Application/ReferenceData/DividendAliasRefreshModels.cs`:

```csharp
namespace WealthIQ.Application.ReferenceData;

/// <summary>A dividend alias row for display/editing in the Stammdaten UI.</summary>
public sealed record DividendAliasView(string NormalizedAlias, string Alias, string Isin);
```

Create `src/WealthIQ.Application/ReferenceData/DividendAliasRefreshService.cs`:

```csharp
using WealthIQ.Application.ReferenceData.Interface;

namespace WealthIQ.Application.ReferenceData;

/// <summary>Validates and persists dividend alias edits (add/update/delete) for the Stammdaten UI.</summary>
public sealed class DividendAliasRefreshService(IDividendAliasStore store)
{
    public async Task SetAsync(string alias, string isin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("Alias must not be blank.", nameof(alias));
        }

        if (string.IsNullOrWhiteSpace(isin))
        {
            throw new ArgumentException("ISIN must not be blank.", nameof(isin));
        }

        store.Upsert(alias, isin);
        await store.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string normalizedAlias, CancellationToken ct = default)
    {
        store.Delete(normalizedAlias);
        await store.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DividendAliasRefreshServiceTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/ReferenceData/DividendAliasRefreshModels.cs src/WealthIQ.Application/ReferenceData/DividendAliasRefreshService.cs tests/WealthIQ.Tests/Application/ReferenceData/DividendAliasRefreshServiceTests.cs
git commit -m "feat(refdata): dividend alias CRUD service for Stammdaten editing"
```

---

## Phase C — Trader's Place importer

### Task 10: CSV parsing helpers (`TradersPlaceCsv`)

**Files:**
- Create: `src/WealthIQ.Infrastructure/TradersPlace/Import/TradersPlaceCsv.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/TradersPlaceCsvTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Infrastructure/TradersPlaceCsvTests.cs`:

```csharp
using System.Text;
using WealthIQ.Infrastructure.TradersPlace.Import;
using Xunit;

namespace WealthIQ.Tests.Infrastructure;

public sealed class TradersPlaceCsvTests
{
    [Fact]
    public void ReadLines_DecodesWindows1252Umlauts()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            // 0xFC = ü, 0xE4 = ä in Windows-1252/Latin1.
            var bytes = new List<byte>();
            bytes.AddRange(Encoding.ASCII.GetBytes("St"));
            bytes.Add(0xFC); // ü
            bytes.AddRange(Encoding.ASCII.GetBytes("ck;W"));
            bytes.Add(0xE4); // ä
            bytes.AddRange(Encoding.ASCII.GetBytes("hrung"));
            File.WriteAllBytes(tmp, bytes.ToArray());

            var lines = TradersPlaceCsv.ReadLines(tmp);

            Assert.Single(lines);
            Assert.Equal("Stück;Währung", lines[0]);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Theory]
    [InlineData("108,259000", 108.259000)]
    [InlineData("14,59", 14.59)]
    [InlineData("-30,78", -30.78)]
    [InlineData("90063,30", 90063.30)]
    public void ParseDecimal_ParsesGermanFormat(string input, double expected)
        => Assert.Equal((decimal)expected, TradersPlaceCsv.ParseDecimal(input));

    [Fact]
    public void ParseDate_ParsesGermanDate()
        => Assert.Equal(new DateOnly(2024, 6, 6), TradersPlaceCsv.ParseDate("06.06.2024"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TradersPlaceCsvTests"`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Create the helper**

Create `src/WealthIQ.Infrastructure/TradersPlace/Import/TradersPlaceCsv.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace WealthIQ.Infrastructure.TradersPlace.Import;

/// <summary>Low-level parsing for Trader's Place CSV exports: Windows-1252 (Latin1) decoding,
/// German decimal/date formats, semicolon separation. Latin1 is built-in and ICU-independent so it
/// works the same on the ubuntu CI runner.</summary>
public static class TradersPlaceCsv
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static IReadOnlyList<string> ReadLines(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Trader's Place CSV not found.", path);
        }

        // Latin1 == ISO-8859-1; the umlauts used by Trader's Place (0xC0–0xFF) coincide with
        // Windows-1252, so this decodes ä/ö/ü/ß correctly without the code-pages package.
        return File.ReadAllLines(path, Encoding.Latin1);
    }

    public static string[] SplitRow(string line) => line.Split(';');

    public static decimal ParseDecimal(string value)
        => decimal.Parse(value.Trim(), NumberStyles.Number | NumberStyles.AllowLeadingSign, German);

    public static bool TryParseDecimal(string? value, out decimal result)
        => decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Number | NumberStyles.AllowLeadingSign, German, out result);

    public static DateOnly ParseDate(string value)
        => DateOnly.ParseExact(value.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static bool TryParseDate(string? value, out DateOnly result)
        => DateOnly.TryParseExact((value ?? string.Empty).Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TradersPlaceCsvTests"`
Expected: PASS (6 cases).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Infrastructure/TradersPlace/Import/TradersPlaceCsv.cs tests/WealthIQ.Tests/Infrastructure/TradersPlaceCsvTests.cs
git commit -m "feat(tradersplace): CSV parsing helpers (Latin1, German number/date, semicolon)"
```

---

### Task 11: `TradersPlaceStatementImporter`

**Files:**
- Create: `src/WealthIQ.Infrastructure/TradersPlace/Import/TradersPlaceStatementImporter.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/TradersPlaceStatementImporterTests.cs` (create)

This is the central component. It implements `IStatementImporter`, resolves all `*.csv` files at the source path (a folder), classifies each by header, routes by transaction type, and produces a unified ledger under `request.AccountId`. It injects `IDividendAliasMap` for dividend resolution.

- [ ] **Step 1: Write the failing tests**

Create `tests/WealthIQ.Tests/Infrastructure/TradersPlaceStatementImporterTests.cs`:

```csharp
using System.Text;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.TradersPlace.Import;
using Xunit;

namespace WealthIQ.Tests.Infrastructure;

public sealed class TradersPlaceStatementImporterTests
{
    private sealed class StubAliasMap : IDividendAliasMap
    {
        private readonly Dictionary<string, string> _map;
        public StubAliasMap(params (string Alias, string Isin)[] entries)
            => _map = entries.ToDictionary(e => DividendAliasNormalizer.Normalize(e.Alias), e => e.Isin);
        public string? ResolveIsin(string alias)
            => _map.TryGetValue(DividendAliasNormalizer.Normalize(alias), out var i) ? i : null;
    }

    private const string DepotHeader =
        "Handelsdatum;Valutadatum;Transaktion;Instrumentenart;WP-Identifikationsart;WP-Identifikation;WP-Name;Nominale / Stück;Kurs / Limit;Handelswährung;Zahlungswährung;Kurswert in Zahlungswährung;Summe der eigenen Spesen in Zahlungswährung;Summe der fremden Spesen in Zahlungswährung;aufgelaufene Stückzinsen in Zahlungswährung;bezahlte / erhaltene KESt in Zahlungswährung;Endbetrag in Zahlungwährung;Währungskurs;Börse;Status;Orderart;Gültigkeit;Lagerland;";

    private const string KontoHeader =
        "Kontonummer;Kontoart;Buchungsdatum;Valutadatum;Transaktion;Währung;Betrag;Kontotext / WP-Identifikation;Umsatz-ID (PK);Ausführungs-ID";

    private static string WriteFolder(params (string Name, string[] Lines)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (name, lines) in files)
        {
            File.WriteAllLines(Path.Combine(dir, name), lines, Encoding.Latin1);
        }
        return dir;
    }

    private static TradersPlaceStatementImporter NewImporter(IDividendAliasMap? aliasMap = null)
        => new(aliasMap ?? new StubAliasMap(("VANGUARD S+P 500U.ETF DLD", "IE00B3XXRP09")));

    private static ImportRequest RequestFor(string dir) => new()
    {
        AccountId = (AccountId)Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Source = new ImportSource(Broker.TradersPlace, Format.CSV, dir)
    };

    [Fact]
    public async Task CanImport_TradersPlaceCsv_True()
    {
        var importer = NewImporter();
        Assert.True(importer.CanImport(new ImportSource(Broker.TradersPlace, Format.CSV, "x")));
        Assert.False(importer.CanImport(new ImportSource(Broker.InteractiveBrokers, Format.XML, "x")));
    }

    [Fact]
    public async Task Import_BuyAndSell_ProducesTradeEntriesWithQuantityPriceFeesKest()
    {
        var dir = WriteFolder(("Depot.csv", new[]
        {
            DepotHeader,
            "02.06.2025;04.06.2025;Kauf;Investmentfonds/ETFs;Isin;IE00B3XXRP09;Vanguard S&P 500 UCITS ETF USD;835,000000;97,888000;EUR;EUR;81736,48;0,00;0,00;0,00;0,00;81736,48;1,000000;MUNC;ausgeführt;Limit;Tagesgültig;Deutschland;",
            "31.10.2025;04.11.2025;Verkauf;Investmentfonds/ETFs;Isin;IE00B3XXRP09;Vanguard S&P 500 UCITS ETF USD;581,000000;112,895000;EUR;EUR;65592,00;0,00;0,00;0,00;340,29;65251,71;1,000000;MUNC;ausgeführt;Limit;Tagesgültig;Deutschland;",
        }));
        try
        {
            var result = await NewImporter().ImportAsync(RequestFor(dir), CancellationToken.None);

            Assert.DoesNotContain(result.Diagnostics, d => d.Severity >= ImportDiagnosticSeverity.Error);
            var trades = result.PortfolioLedger.Entries.OfType<TradeEntry>().ToList();
            Assert.Equal(2, trades.Count);

            var buy = Assert.Single(trades, t => t.Side == TradeSide.Buy);
            Assert.Equal(835m, buy.Quantity.Value);
            Assert.Equal(97.888m, buy.UnitPrice.Amount);

            var sell = Assert.Single(trades, t => t.Side == TradeSide.Sell);
            Assert.Equal(581m, sell.Quantity.Value);
            Assert.Equal(112.895m, sell.UnitPrice.Amount);
            Assert.Equal(340.29m, sell.WithheldTax.Amount);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Import_Dividend_ResolvesIsinViaAliasMap()
    {
        var dir = WriteFolder(("Konto.csv", new[]
        {
            KontoHeader,
            "4415066002;WP-Verrechnungskonto;03.07.2025;02.07.2025;Effekten;EUR;221,36;VANGUARD S+P 500U.ETF DLD;K483225;",
        }));
        try
        {
            var result = await NewImporter().ImportAsync(RequestFor(dir), CancellationToken.None);

            Assert.DoesNotContain(result.Diagnostics, d => d.Severity >= ImportDiagnosticSeverity.Error);
            var cash = Assert.Single(result.PortfolioLedger.Entries.OfType<CashEntry>());
            Assert.Equal(CashFlowType.Dividend, cash.CashFlowType);
            Assert.Equal(221.36m, cash.GrossAmount.Amount);
            var related = result.PortfolioLedger.Instruments.Single(i => i.ISIN == "IE00B3XXRP09");
            Assert.Equal(related.InstrumentId, cash.RelatedInstrumentId);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Import_UnmappedDividend_ProducesBlockingError()
    {
        var dir = WriteFolder(("Konto.csv", new[]
        {
            KontoHeader,
            "4415066002;WP-Verrechnungskonto;30.12.2025;24.12.2025;Effekten;EUR;399,29;ISHSIV-DL T.BD20+YR DL D;K739837;",
        }));
        try
        {
            var result = await NewImporter().ImportAsync(RequestFor(dir), CancellationToken.None);
            Assert.Contains(result.Diagnostics,
                d => d.Severity >= ImportDiagnosticSeverity.Error && d.Code == ImportDiagnosticCode.MalformedField);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Import_KontoabschlussPositive_IsInterest_NegativeIsSkipped()
    {
        var dir = WriteFolder(("Konto.csv", new[]
        {
            KontoHeader,
            "4415066002;WP-Verrechnungskonto;28.06.2024;30.06.2024;Kontoabschluss;EUR;14,59;Abschluss;K78297;",
            "4415066002;WP-Verrechnungskonto;30.06.2025;30.06.2025;Kontoabschluss;EUR;-30,78;Abschluss;K468495;",
        }));
        try
        {
            var result = await NewImporter().ImportAsync(RequestFor(dir), CancellationToken.None);
            var cash = result.PortfolioLedger.Entries.OfType<CashEntry>().ToList();
            var interest = Assert.Single(cash, c => c.CashFlowType == CashFlowType.Interest);
            Assert.Equal(14.59m, interest.GrossAmount.Amount);
            Assert.DoesNotContain(cash, c => c.GrossAmount.Amount < 0m);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Import_TradeRowsInKontoumsaetze_AreSkipped_NoDoubleCount()
    {
        var dir = WriteFolder(
            ("Depot.csv", new[]
            {
                DepotHeader,
                "06.06.2024;10.06.2024;Kauf;Investmentfonds/ETFs;Isin;IE00B3XXRP09;Vanguard S&P 500 UCITS ETF USD;100,000000;108,259000;EUR;EUR;10825,90;0,00;0,00;0,00;0,00;10825,90;1,000000;MUNC;ausgeführt;Limit;Tagesgültig;Deutschland;",
            }),
            ("Konto.csv", new[]
            {
                KontoHeader,
                "4415066002;WP-Verrechnungskonto;06.06.2024;10.06.2024;Kauf;EUR;-10825,9;IE00B3XXRP09, Vanguard S&P 500 UCITS ETF USD;;158816",
                "4415066002;WP-Verrechnungskonto;05.06.2024;05.06.2024;Gutschrift;EUR;50000;Sebastian Brandt;K63157;",
            }));
        try
        {
            var result = await NewImporter().ImportAsync(RequestFor(dir), CancellationToken.None);
            // exactly one trade (from Depot), zero from Konto; Gutschrift skipped.
            Assert.Single(result.PortfolioLedger.Entries.OfType<TradeEntry>());
            Assert.Empty(result.PortfolioLedger.Entries.OfType<CashEntry>());
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TradersPlaceStatementImporterTests"`
Expected: FAIL — importer does not exist.

- [ ] **Step 3: Implement the importer**

Create `src/WealthIQ.Infrastructure/TradersPlace/Import/TradersPlaceStatementImporter.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Import.Interface;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.TradersPlace.Import;

/// <summary>
/// Imports Trader's Place CSV exports (spec 2026-06-06). Ingests BOTH the Depotumsätze (trades) and
/// Kontoumsätze (cash) files in one pass, classifying each by header signature and routing by
/// transaction type so trade rows that appear in both files are never double-counted. All entries are
/// produced under the single requested account.
/// </summary>
public sealed class TradersPlaceStatementImporter(IDividendAliasMap dividendAliasMap) : IStatementImporter
{
    public bool CanImport(ImportSource source)
        => source is not null
           && source.Broker == Broker.TradersPlace
           && source.Format == Format.CSV;

    public Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new ImportResult();

        if (!CanImport(request.Source))
        {
            result.Diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Fatal, ImportDiagnosticCode.UnsupportedSource,
                $"Unsupported import source '{request.Source.Broker}/{request.Source.Format}'."));
            return Task.FromResult(result);
        }

        var files = ResolveFiles(request.Source.FilePath);
        if (files.Count == 0)
        {
            result.Diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Fatal, ImportDiagnosticCode.InputPathNotFound,
                $"No CSV files found at '{request.Source.FilePath}'."));
            return Task.FromResult(result);
        }

        var instrumentCatalog = new Dictionary<InstrumentId, Instrument>();
        var entries = new List<PortfolioEntry>();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var lines = TradersPlaceCsv.ReadLines(file);
            if (lines.Count == 0)
            {
                continue;
            }

            var header = lines[0];
            if (header.StartsWith("Handelsdatum;", StringComparison.Ordinal))
            {
                ParseDepotumsaetze(lines, file, request.AccountId, instrumentCatalog, entries, result.Diagnostics);
            }
            else if (header.StartsWith("Kontonummer;", StringComparison.Ordinal))
            {
                ParseKontoumsaetze(lines, file, request.AccountId, instrumentCatalog, entries, result.Diagnostics);
            }
            else
            {
                result.Diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Fatal, ImportDiagnosticCode.UnsupportedSource,
                    $"Unrecognized Trader's Place CSV header in '{Path.GetFileName(file)}'.",
                    SourceReference: file));
            }
        }

        result.Instruments = instrumentCatalog.Values.OrderBy(x => x.Symbol).ThenBy(x => x.ISIN).ToList();
        result.PortfolioLedger = new PortfolioLedger(
            entries.OrderBy(x => x.OccurredAt).ToList(), result.Instruments);
        return Task.FromResult(result);
    }

    private static List<string> ResolveFiles(string inputPath)
    {
        if (Directory.Exists(inputPath))
        {
            return Directory.GetFiles(inputPath, "*.csv", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        return File.Exists(inputPath) ? new List<string> { inputPath } : new List<string>();
    }

    // --- Depotumsätze (trades) ---
    private void ParseDepotumsaetze(
        IReadOnlyList<string> lines, string file, AccountId accountId,
        Dictionary<InstrumentId, Instrument> catalog, List<PortfolioEntry> entries, List<ImportDiagnostic> diagnostics)
    {
        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Depotumsätze;", StringComparison.Ordinal))
            {
                continue; // blank or footer
            }

            var c = TradersPlaceCsv.SplitRow(line);
            if (c.Length < 17)
            {
                diagnostics.Add(Warn(ImportDiagnosticCode.InvalidRecord, $"Skipped malformed trade row {i + 1}.", file));
                continue;
            }

            var transaktion = c[2].Trim();
            var side = transaktion switch
            {
                "Kauf" => TradeSide.Buy,
                "Verkauf" => TradeSide.Sell,
                _ => (TradeSide?)null
            };
            if (side is null)
            {
                diagnostics.Add(Warn(ImportDiagnosticCode.InvalidRecord, $"Skipped unknown trade transaction '{transaktion}' (row {i + 1}).", file));
                continue;
            }

            var isin = c[5].Trim();
            var name = c[6].Trim();
            if (isin.Length == 0)
            {
                diagnostics.Add(Error(ImportDiagnosticCode.MalformedField, $"Trade row {i + 1} has no ISIN.", file));
                continue;
            }

            if (!TradersPlaceCsv.TryParseDate(c[0], out var handelsdatum)
                || !TradersPlaceCsv.TryParseDecimal(c[7], out var quantity)
                || !TradersPlaceCsv.TryParseDecimal(c[8], out var price))
            {
                diagnostics.Add(Error(ImportDiagnosticCode.MalformedField, $"Trade row {i + 1} has an unparseable date/quantity/price.", file));
                continue;
            }

            if (quantity <= 0m || price <= 0m)
            {
                diagnostics.Add(Error(ImportDiagnosticCode.MalformedField, $"Trade row {i + 1} has non-positive quantity or price.", file));
                continue;
            }

            var tradeCurrency = ParseCurrency(c[9].Trim(), diagnostics, file, i + 1);
            var paymentCurrency = ParseCurrency(c[10].Trim(), diagnostics, file, i + 1);
            if (tradeCurrency is null || paymentCurrency is null)
            {
                continue;
            }

            TradersPlaceCsv.TryParseDecimal(c[12], out var ownFees);
            TradersPlaceCsv.TryParseDecimal(c[13], out var foreignFees);
            TradersPlaceCsv.TryParseDecimal(c[15], out var kest);

            var instrument = EnsureInstrument(catalog, isin, name);
            var occurredAt = new DateTimeOffset(handelsdatum.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));

            var reference = StableTradeReference(handelsdatum, isin, transaktion, c[7].Trim(), c[8].Trim(), c[16].Trim(), i);

            entries.Add(new TradeEntry(
                PortfolioEntryId.NewId(), accountId, occurredAt, handelsdatum,
                new SourceProvenance
                {
                    SourceSystem = "TradersPlace",
                    ImportFormat = "CSV",
                    SourceLocation = file,
                    SourceRecordReference = reference,
                    SourceSection = "Depotumsätze"
                },
                instrument.InstrumentId, side.Value, new Quantity(quantity),
                new Money(price, tradeCurrency.Value),
                new Money(Math.Abs(ownFees) + Math.Abs(foreignFees), paymentCurrency.Value),
                new Money(0m, paymentCurrency.Value),
                new Money(Math.Abs(kest), paymentCurrency.Value)));
        }
    }

    // --- Kontoumsätze (cash) ---
    private void ParseKontoumsaetze(
        IReadOnlyList<string> lines, string file, AccountId accountId,
        Dictionary<InstrumentId, Instrument> catalog, List<PortfolioEntry> entries, List<ImportDiagnostic> diagnostics)
    {
        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Kontoumsätze;", StringComparison.Ordinal))
            {
                continue; // blank or footer
            }

            var c = TradersPlaceCsv.SplitRow(line);
            if (c.Length < 9)
            {
                diagnostics.Add(Warn(ImportDiagnosticCode.InvalidRecord, $"Skipped malformed cash row {i + 1}.", file));
                continue;
            }

            var transaktion = c[4].Trim();
            var reference = c[8].Trim(); // Umsatz-ID (PK)

            // Cash movements + trade rows are handled elsewhere / not taxable → skip.
            if (transaktion is "Gutschrift" or "Überweisung" or "Einzahlung" or "Kauf" or "Verkauf")
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Info, ImportDiagnosticCode.IgnoredAsset,
                    $"Ignored '{transaktion}' (not a taxable event in this import).", SourceReference: reference));
                continue;
            }

            if (!TradersPlaceCsv.TryParseDate(c[2], out var buchungsdatum)
                || !TradersPlaceCsv.TryParseDecimal(c[6], out var amount))
            {
                diagnostics.Add(Error(ImportDiagnosticCode.MalformedField, $"Cash row {i + 1} has an unparseable date/amount.", file));
                continue;
            }

            var currency = ParseCurrency(c[5].Trim(), diagnostics, file, i + 1);
            if (currency is null)
            {
                continue;
            }

            var occurredAt = new DateTimeOffset(buchungsdatum.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
            var text = c[7].Trim();

            var provenance = new SourceProvenance
            {
                SourceSystem = "TradersPlace",
                ImportFormat = "CSV",
                SourceLocation = file,
                SourceRecordReference = reference,
                SourceSection = "Kontoumsätze"
            };

            if (transaktion == "Effekten")
            {
                var isin = dividendAliasMap.ResolveIsin(text);
                if (isin is null)
                {
                    diagnostics.Add(Error(ImportDiagnosticCode.MalformedField,
                        $"Dividend alias '{text}' (row {i + 1}) is not mapped to an ISIN. Add it under Stammdaten.", file));
                    continue;
                }

                var cashInstrument = EnsureCashInstrument(catalog, currency.Value);
                var related = EnsureInstrument(catalog, isin, text);
                entries.Add(new CashEntry(
                    PortfolioEntryId.NewId(), accountId, occurredAt, buchungsdatum, provenance,
                    cashInstrument.InstrumentId, WealthIQ.Domain.Enumeration.CashFlowType.Dividend,
                    new Money(amount, currency.Value), new Money(0m, currency.Value), new Money(0m, currency.Value),
                    related.InstrumentId));
                continue;
            }

            if (transaktion == "Kontoabschluss")
            {
                if (amount <= 0m)
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Info, ImportDiagnosticCode.IgnoredAsset,
                        $"Ignored non-positive Kontoabschluss (debit interest/fee) row {i + 1}.", SourceReference: reference));
                    continue;
                }

                var cashInstrument = EnsureCashInstrument(catalog, currency.Value);
                entries.Add(new CashEntry(
                    PortfolioEntryId.NewId(), accountId, occurredAt, buchungsdatum, provenance,
                    cashInstrument.InstrumentId, WealthIQ.Domain.Enumeration.CashFlowType.Interest,
                    new Money(amount, currency.Value), new Money(0m, currency.Value), new Money(0m, currency.Value)));
                continue;
            }

            diagnostics.Add(Warn(ImportDiagnosticCode.InvalidRecord, $"Skipped unknown cash transaction '{transaktion}' (row {i + 1}).", file));
        }
    }

    private static Instrument EnsureInstrument(Dictionary<InstrumentId, Instrument> catalog, string isin, string name)
    {
        var id = StableInstrumentId(isin);
        if (!catalog.ContainsKey(id))
        {
            catalog[id] = new Instrument(id, isin, isin, string.IsNullOrWhiteSpace(name) ? isin : name, 0m);
        }

        return catalog[id];
    }

    private static Instrument EnsureCashInstrument(Dictionary<InstrumentId, Instrument> catalog, CurrencyCode currency)
    {
        var symbol = currency.ToString();
        var id = StableInstrumentId($"CASH:{symbol}");
        if (!catalog.ContainsKey(id))
        {
            catalog[id] = new Instrument(id, string.Empty, symbol, $"{symbol} cash", 0m);
        }

        return catalog[id];
    }

    private static CurrencyCode? ParseCurrency(string currency, List<ImportDiagnostic> diagnostics, string file, int row)
    {
        if (Enum.TryParse<CurrencyCode>(currency, true, out var parsed))
        {
            return parsed;
        }

        diagnostics.Add(Error(ImportDiagnosticCode.MalformedField, $"Unsupported currency '{currency}' (row {row}).", file));
        return null;
    }

    private static string StableTradeReference(DateOnly date, string isin, string transaktion, string qty, string price, string endbetrag, int rowIndex)
    {
        var key = $"{date:yyyy-MM-dd}|{isin}|{transaktion}|{qty}|{price}|{endbetrag}|{rowIndex}";
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
        return $"TP-DEPOT-{new Guid(bytes):N}";
    }

    private static InstrumentId StableInstrumentId(string key)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
        return (InstrumentId)new Guid(bytes);
    }

    private static ImportDiagnostic Warn(ImportDiagnosticCode code, string message, string file)
        => new(ImportDiagnosticSeverity.Warning, code, message, SourceReference: file);

    private static ImportDiagnostic Error(ImportDiagnosticCode code, string message, string file)
        => new(ImportDiagnosticSeverity.Error, code, message, SourceReference: file);
}
```

> **Verify before coding:** open `src/WealthIQ.Application/Import/Diagnostic/ImportDiagnosticCode.cs` and confirm the codes `UnsupportedSource`, `InputPathNotFound`, `InvalidRecord`, `MalformedField`, `IgnoredAsset` exist (they are all used by `IbkrStatementImporter`). If any name differs, use the actual enum member. Also confirm the `ImportDiagnostic` constructor's parameter names (`SourceReference`, `Field`) by reading `ImportDiagnostic.cs`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TradersPlaceStatementImporterTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Infrastructure/TradersPlace/Import/TradersPlaceStatementImporter.cs tests/WealthIQ.Tests/Infrastructure/TradersPlaceStatementImporterTests.cs
git commit -m "feat(tradersplace): statement importer (header routing, KESt, alias dividends)"
```

---

## Phase D — Import plumbing for two files

### Task 12: `IRawFileStore.IngestDirectory`

**Files:**
- Modify: `src/WealthIQ.Application/Persistence/Interface/IRawFileStore.cs`
- Modify: `src/WealthIQ.Infrastructure/Ingest/FileSystemRawFileStore.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/FileSystemRawFileStoreTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Infrastructure/FileSystemRawFileStoreTests.cs`:

```csharp
using WealthIQ.Infrastructure.Ingest;
using Xunit;

namespace WealthIQ.Tests.Infrastructure;

public sealed class FileSystemRawFileStoreTests
{
    [Fact]
    public void IngestDirectory_CopiesAllFilesIntoAnIsolatedSubfolder()
    {
        var src = Path.Combine(Path.GetTempPath(), "tp-src-" + Guid.NewGuid().ToString("N"));
        var audit = Path.Combine(Path.GetTempPath(), "tp-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "Depot.csv"), "a");
        File.WriteAllText(Path.Combine(src, "Konto.csv"), "b");
        try
        {
            var store = new FileSystemRawFileStore(audit);
            var storedDir = store.IngestDirectory(src);

            Assert.True(Directory.Exists(storedDir));
            Assert.StartsWith(audit, storedDir);
            Assert.Equal(2, Directory.GetFiles(storedDir, "*.csv").Length);
        }
        finally
        {
            Directory.Delete(src, true);
            if (Directory.Exists(audit)) Directory.Delete(audit, true);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~FileSystemRawFileStoreTests"`
Expected: FAIL — no `IngestDirectory`.

- [ ] **Step 3: Add the method**

In `src/WealthIQ.Application/Persistence/Interface/IRawFileStore.cs`, add:

```csharp
    /// <summary>Copies every file from a source directory into an isolated audit subfolder and returns
    /// that subfolder. Used for multi-file imports (e.g. Trader's Place: trades + cash CSVs together).</summary>
    string IngestDirectory(string sourceDirectory);
```

In `src/WealthIQ.Infrastructure/Ingest/FileSystemRawFileStore.cs`, add:

```csharp
    public string IngestDirectory(string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Raw statement directory not found: {sourceDirectory}");
        }

        Directory.CreateDirectory(rootFolder);
        var subfolder = Path.Combine(rootFolder, $"import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(subfolder);
        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(subfolder, Path.GetFileName(file)), overwrite: true);
        }

        return subfolder;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~FileSystemRawFileStoreTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/Persistence/Interface/IRawFileStore.cs src/WealthIQ.Infrastructure/Ingest/FileSystemRawFileStore.cs tests/WealthIQ.Tests/Infrastructure/FileSystemRawFileStoreTests.cs
git commit -m "feat(ingest): IRawFileStore.IngestDirectory for multi-file imports"
```

---

### Task 13: Pipeline — importer selection + directory ingest

**Files:**
- Modify: `src/WealthIQ.Application/Import/StatementImportPipeline.cs`
- Test: `tests/WealthIQ.Tests/Application/Import/StatementImportPipelineTests.cs` (create or extend)

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Application/Import/StatementImportPipelineSelectionTests.cs`:

```csharp
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Import.Interface;
using WealthIQ.Application.Persistence;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using Xunit;

namespace WealthIQ.Tests.Application.Import;

public sealed class StatementImportPipelineSelectionTests
{
    private sealed class FakeRawFileStore : IRawFileStore
    {
        public string Ingest(string sourceFilePath) => sourceFilePath;
        public string IngestDirectory(string sourceDirectory) => sourceDirectory;
    }

    private sealed class FakeImportStore : IImportStore
    {
        public Task PersistFailedImportAsync(ImportBatch batch, IReadOnlyList<WealthIQ.Application.Import.Diagnostic.ImportDiagnostic> d, CancellationToken ct) => Task.CompletedTask;
        public Task<ImportPersistCounts> PersistImportAsync(ImportBatch batch, PortfolioLedger ledger, IReadOnlyList<WealthIQ.Application.Import.Diagnostic.ImportDiagnostic> d, CancellationToken ct)
            => Task.FromResult(new ImportPersistCounts(ledger.Entries.Count, 0));
    }

    private sealed class TpImporter : IStatementImporter
    {
        public bool CanImport(ImportSource s) => s.Broker == Broker.TradersPlace;
        public Task<ImportResult> ImportAsync(ImportRequest r, CancellationToken ct)
            => Task.FromResult(new ImportResult());
    }

    private sealed class IbkrImporter : IStatementImporter
    {
        public bool CanImport(ImportSource s) => s.Broker == Broker.InteractiveBrokers;
        public Task<ImportResult> ImportAsync(ImportRequest r, CancellationToken ct)
            => throw new InvalidOperationException("Wrong importer selected.");
    }

    [Fact]
    public async Task Run_SelectsImporterByCanImport_AndIngestsDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tp-pl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Depot.csv"), "x");
        try
        {
            var accountId = AccountId.NewId();
            var pipeline = new StatementImportPipeline(
                new IStatementImporter[] { new IbkrImporter(), new TpImporter() },
                new FakeRawFileStore(), new FakeImportStore(), TimeProvider.System);

            var command = new ImportStatementCommand(
                new ImportRequest
                {
                    Source = new ImportSource(Broker.TradersPlace, Format.CSV, dir),
                    AccountId = accountId
                },
                new Account(accountId, "TP-1"));

            var result = await pipeline.RunAsync(command);
            Assert.Equal(ImportStatus.Committed, result.Status);
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

> **Verify before coding:** confirm `IImportStore`'s method names/signatures (`PersistFailedImportAsync`, `PersistImportAsync`) and `ImportPersistCounts`'s constructor by reading those files; adjust the fakes to match exactly.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~StatementImportPipelineSelectionTests"`
Expected: FAIL — `StatementImportPipeline` ctor takes a single `IStatementImporter`, not a collection.

- [ ] **Step 3: Change the pipeline to select an importer + ingest directories**

In `src/WealthIQ.Application/Import/StatementImportPipeline.cs`:

Change the primary constructor parameter from `IStatementImporter importer` to `IEnumerable<IStatementImporter> importers`. At the top of `RunAsync`, select the importer and handle the directory case. Replace the ingest + import section:

```csharp
    public async Task<ImportPipelineResult> RunAsync(ImportStatementCommand command, CancellationToken ct = default)
    {
        var batchId = Guid.NewGuid();
        var importedAt = timeProvider.GetUtcNow();

        var importer = importers.FirstOrDefault(i => i.CanImport(command.Request.Source));
        if (importer is null)
        {
            var diagnostic = new ImportDiagnostic(
                ImportDiagnosticSeverity.Fatal,
                ImportDiagnosticCode.UnsupportedSource,
                $"No importer supports '{command.Request.Source.Broker}/{command.Request.Source.Format}'.");
            return new ImportPipelineResult(ImportStatus.Aborted, batchId, 0, 0, new[] { diagnostic });
        }

        string storedPath;
        try
        {
            storedPath = Directory.Exists(command.Request.Source.FilePath)
                ? rawFileStore.IngestDirectory(command.Request.Source.FilePath)
                : rawFileStore.Ingest(command.Request.Source.FilePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            var diagnostic = new ImportDiagnostic(
                ImportDiagnosticSeverity.Fatal,
                ImportDiagnosticCode.InputPathNotFound,
                ex.Message);
            return new ImportPipelineResult(ImportStatus.Aborted, batchId, 0, 0, new[] { diagnostic });
        }

        var ingestedRequest = command.Request with
        {
            Source = command.Request.Source with { FilePath = storedPath }
        };

        var importResult = await importer.ImportAsync(ingestedRequest, ct);
```

(Leave the remainder of the method — blocking-diagnostic handling, ledger build, persist — unchanged.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~StatementImportPipeline"`
Expected: PASS. The Web won't compile until DI is updated in Task 16 — run filtered tests only.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/Import/StatementImportPipeline.cs tests/WealthIQ.Tests/Application/Import/StatementImportPipelineSelectionTests.cs
git commit -m "feat(import): pipeline selects importer by CanImport and ingests multi-file directories"
```

---

## Phase E — Reference data: instrument profiles

### Task 14: Add the 4 missing Trader's Place ISINs to `instruments.json`

**Files:**
- Modify: `data/reference/instruments.json`

- [ ] **Step 1: Add the profiles**

Open `data/reference/instruments.json` and add these four entries (merge into the existing JSON object; do not remove existing keys). Xetra-Gold is deliberately `subject_to_vorabpauschale: false` and `tfs_quote: 0.00` — its tax-free-after-1-year treatment is **deferred** (spec §11), so for now it is taxed as an ordinary gain:

```json
  "FR0010510800": {
    "name": "Amundi EUR Overnight Return UCITS ETF Acc",
    "type": "ETF_MONEY_MARKET",
    "tfs_quote": 0.00,
    "subject_to_vorabpauschale": true
  },
  "FR0010342592": {
    "name": "Amundi Nasdaq-100 Daily (2x) Leveraged UCITS ETF Acc",
    "type": "ETF_EQUITY",
    "tfs_quote": 0.30,
    "subject_to_vorabpauschale": true
  },
  "IE00B53SZB19": {
    "name": "iShares NASDAQ 100 UCITS ETF USD Acc",
    "type": "ETF_EQUITY",
    "tfs_quote": 0.30,
    "subject_to_vorabpauschale": true
  },
  "DE000A0S9GB0": {
    "name": "Xetra-Gold",
    "type": "ETC",
    "tfs_quote": 0.00,
    "subject_to_vorabpauschale": false
  }
```

> `IE00B3XXRP09` (Vanguard S&P 500) and `IE00BSKRJZ44` (iShares USD Treasury 20+yr) already exist — do not duplicate them.

- [ ] **Step 2: Validate JSON**

Run: `node -e "JSON.parse(require('fs').readFileSync('data/reference/instruments.json','utf8')); console.log('ok')"`
Expected: `ok`. (If `node` is unavailable, use `dotnet`/any JSON validator, or rely on the build/seed step which will throw on malformed JSON.)

- [ ] **Step 3: Commit**

```bash
git add data/reference/instruments.json
git commit -m "feat(refdata): instrument profiles for Trader's Place ISINs (Xetra-Gold tax-free deferred)"
```

---

## Phase F — DI wiring + Web

### Task 15: DI wiring in `Program.cs`

**Files:**
- Modify: `src/WealthIQ.Web/Program.cs`

- [ ] **Step 1: Register the second importer, alias map/store/service**

In `src/WealthIQ.Web/Program.cs`:

Add to the using block:

```csharp
using WealthIQ.Infrastructure.TradersPlace.Import;
```

Replace the single importer registration (line ~83) with both importers (DI injects `IEnumerable<IStatementImporter>` into the pipeline automatically):

```csharp
builder.Services.AddScoped<IStatementImporter, IbkrStatementImporter>();
builder.Services.AddScoped<IStatementImporter, TradersPlaceStatementImporter>();
builder.Services.AddScoped<StatementImportPipeline>();
```

In the reference-data section, register the alias map/store/service:

```csharp
builder.Services.AddScoped<IDividendAliasMap, DbDividendAliasMap>();
builder.Services.AddScoped<IDividendAliasStore, DbDividendAliasStore>();
builder.Services.AddScoped<DividendAliasRefreshService>();
```

Add the needed usings (if not already present):

```csharp
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Infrastructure.ReferenceData;
```

> `IbkrStatementImporter` is currently registered without constructor args; `TradersPlaceStatementImporter` needs `IDividendAliasMap`, which is registered above, so the default DI activator resolves it.

- [ ] **Step 2: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeds (Web now compiles against the new pipeline ctor and report shape only after Task 16/17 — if the build fails solely in `Import.razor`/`Steuerreport.razor`, proceed to those tasks; this step verifies Program.cs itself compiles, so run after Tasks 16–17 if needed).

> Execution note: Program.cs, Import.razor and Steuerreport.razor are interdependent for compilation. Do Tasks 15→16→17, then build once at the end of Task 17.

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Web/Program.cs
git commit -m "feat(web): DI for Trader's Place importer and dividend alias services"
```

---

### Task 16: Import page — broker selector + two-file Trader's Place flow

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Import.razor`

- [ ] **Step 1: Add a broker selector and Trader's Place branch**

Edit `src/WealthIQ.Web/Components/Pages/Import.razor`. Add a broker `MudSelect` above the account field, and branch the import so Trader's Place stages both files into one temp folder and runs a single command with `Format.CSV` and `Broker.TradersPlace`. Replace the `@code` `RunImport` account/command construction and add broker state.

Add near the top of `@code` (with the other fields):

```csharp
    private Broker _broker = Broker.InteractiveBrokers;
```

Add the selector in the card (above the account `MudTextField`):

```razor
        <MudSelect T="Broker" @bind-Value="_broker" Label="Broker" Variant="Variant.Outlined"
                   Dense="true" Class="mb-4">
            <MudSelectItem T="Broker" Value="Broker.InteractiveBrokers">Interactive Brokers (XML)</MudSelectItem>
            <MudSelectItem T="Broker" Value="Broker.TradersPlace">Trader's Place (CSV: Depot- + Kontoumsätze)</MudSelectItem>
        </MudSelect>
```

Update the file input `accept` to depend on the broker:

```razor
            <InputFile @key="_fileInputKey" OnChange="OnFilesSelected" accept="@(_broker == Broker.TradersPlace ? ".csv" : ".xml")" multiple />
```

Replace `RunImport` so Trader's Place uses one combined command. Replace the whole method with:

```csharp
    private async Task RunImport()
    {
        if (_pendingFiles.Count == 0 || string.IsNullOrWhiteSpace(_accountNumber))
            return;

        _busy = true;
        _doneCount = 0;
        _error = null;
        _results.Clear();

        var brokerName = _broker.ToString();
        var accountId = DeterministicAccount.IdFor(brokerName, _accountNumber.Trim());
        var account = new Account(accountId, _accountNumber.Trim());

        var toProcess = _pendingFiles.ToList();
        _pendingFiles.Clear();

        try
        {
            if (_broker == Broker.TradersPlace)
            {
                _totalCount = 1;
                var stagingDir = Path.Combine(Path.GetTempPath(), $"wealthiq-tp-{Guid.NewGuid():N}");
                Directory.CreateDirectory(stagingDir);
                try
                {
                    foreach (var (name, tempPath) in toProcess)
                        File.Copy(tempPath, Path.Combine(stagingDir, name), overwrite: true);

                    var command = new ImportStatementCommand(
                        new ImportRequest
                        {
                            Source = new ImportSource(Broker.TradersPlace, Format.CSV, stagingDir),
                            AccountId = accountId
                        },
                        account);

                    var result = await Pipeline.RunAsync(command);
                    _results.Add((toProcess.Count == 1 ? toProcess[0].Name : $"{toProcess.Count} Dateien", result));
                }
                catch (Exception ex)
                {
                    _error = $"Trader's Place Import fehlgeschlagen — {ex.Message}";
                }
                finally
                {
                    foreach (var (_, tempPath) in toProcess)
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                    if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                }

                _doneCount = 1;
                StateHasChanged();
            }
            else
            {
                _totalCount = toProcess.Count;
                foreach (var (name, tempPath) in toProcess)
                {
                    try
                    {
                        var command = new ImportStatementCommand(
                            new ImportRequest
                            {
                                Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, tempPath),
                                AccountId = accountId
                            },
                            account);

                        var result = await Pipeline.RunAsync(command);
                        _results.Add((name, result));
                    }
                    catch (Exception ex)
                    {
                        _error = $"{name}: Import fehlgeschlagen — {ex.Message}";
                    }
                    finally
                    {
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                    }

                    _doneCount++;
                    StateHasChanged();
                }
            }
        }
        finally
        {
            _busy = false;
            _fileInputKey++;
        }
    }
```

Update the page subtitle/header text to reflect both brokers, e.g. change `Subtitle="IBKR FlexQuery-Statements (XML) einlesen"` to `Subtitle="Broker-Statements einlesen (IBKR XML · Trader's Place CSV)"`.

- [ ] **Step 2: (deferred build)** — build together with Task 17.

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Import.razor
git commit -m "feat(web): import page broker selector + Trader's Place two-file flow"
```

---

### Task 17: Steuerreport — account dropdown + KESt display

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Steuerreport.razor`

- [ ] **Step 1: Bind the page to the per-account report shape**

In `src/WealthIQ.Web/Components/Pages/Steuerreport.razor`:

Add `@using WealthIQ.Application.Tax.Report;` is already present. Change the state fields and `OnInitializedAsync` in `@code`:

```csharp
    private bool _loading = true;
    private string? _error;
    private IReadOnlyList<AccountTaxReport> _accounts = Array.Empty<AccountTaxReport>();
    private Guid _selectedAccountId;
    private int _selectedYear;

    private AccountTaxReport? CurrentAccount => _accounts.FirstOrDefault(a => a.AccountId == _selectedAccountId);
    private IReadOnlyList<AnnualTaxReport> _reports => CurrentAccount?.Years ?? Array.Empty<AnnualTaxReport>();
    private AnnualTaxReport? Current => _reports.FirstOrDefault(r => r.Year == _selectedYear);
```

> `_reports` becomes a computed property; the rest of the file already reads `_reports`/`Current` and keeps working.

Replace `OnInitializedAsync`:

```csharp
    protected override async Task OnInitializedAsync()
    {
        try
        {
            _accounts = await ReportService.GenerateAsync();
            if (_accounts.Count > 0)
            {
                _selectedAccountId = _accounts[0].AccountId;
                _selectedYear = _reports.Count > 0 ? _reports[^1].Year : 0;
            }
        }
        catch (Exception ex)
        {
            _error = $"Berechnung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }
```

Add an account-changed handler near `OnYearChanged`:

```csharp
    private void OnAccountChanged(Guid accountId)
    {
        _selectedAccountId = accountId;
        _selectedYear = _reports.Count > 0 ? _reports[^1].Year : 0;
        _animatePending = true;
    }
```

Update the empty-state guard: change `else if (_reports.Count == 0)` to `else if (_accounts.Count == 0)`.

- [ ] **Step 2: Add the account dropdown to the header Actions**

In the `<PageHeader>` `<Actions>` block, add an account selector before the year selector:

```razor
        @if (_accounts.Count > 1)
        {
            <MudSelect T="Guid" Value="_selectedAccountId" ValueChanged="OnAccountChanged" Label="Konto"
                       Variant="Variant.Outlined" Dense="true" Style="min-width:180px;" Class="me-2">
                @foreach (var acct in _accounts)
                {
                    <MudSelectItem T="Guid" Value="acct.AccountId">@acct.AccountNumber</MudSelectItem>
                }
            </MudSelect>
        }
```

- [ ] **Step 3: Show withheld KESt in the KPI grid**

In the KPI `MudGrid`, add a StatCard for KESt (after the "Anrechenbare Quellensteuer" item):

```razor
        <MudItem xs="12" sm="6" md="4"><StatCard Caption="Einbehaltene KESt" Value="@Current.Summary.WithheldKESt" CountUp="true" /></MudItem>
```

- [ ] **Step 4: Build the whole solution**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeds across all projects (Program.cs, Import.razor, Steuerreport.razor now consistent with the Application changes).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Steuerreport.razor
git commit -m "feat(web): per-account Steuerreport dropdown + withheld KESt KPI"
```

---

### Task 18: Stammdaten — dividend alias editing panel

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/DataAdmin.razor`

- [ ] **Step 1: Read the existing Basiszins panel for the pattern**

Run: `dotnet build WealthIQ.slnx` is not needed here; instead open `src/WealthIQ.Web/Components/Pages/DataAdmin.razor` and locate the Basiszins `MudTable` with `RowEditCommit` + per-row delete + inline add-row. Mirror it for dividend aliases.

- [ ] **Step 2: Add the alias panel**

Inject the services at the top of `DataAdmin.razor` (with the other `@inject` lines):

```razor
@inject WealthIQ.Infrastructure.Persistence.WealthIqDbContext Db
@inject WealthIQ.Application.ReferenceData.DividendAliasRefreshService AliasService
```

> `WealthIqDbContext` may already be injected on this page (other panels query it directly). If so, do not add a duplicate `@inject`.

Add a section card with an editable table. Place it alongside the existing panels:

```razor
<div style="margin-top:16px;">
<SectionCard Title="Trader's Place Dividenden-Zuordnung (Alias → ISIN)">
    <MudTable Items="_aliases" Dense="true" Hover="true" Elevation="0" CanCancelEdit="true"
              RowEditCommit="OnAliasCommit" T="WealthIQ.Application.ReferenceData.DividendAliasView">
        <HeaderContent>
            <MudTh>Alias</MudTh>
            <MudTh>ISIN</MudTh>
            <MudTh></MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd DataLabel="Alias">@context.Alias</MudTd>
            <MudTd DataLabel="ISIN">@context.Isin</MudTd>
            <MudTd>
                <MudIconButton Icon="@Icons.Material.Outlined.Delete" Size="Size.Small"
                               aria-label="Löschen" OnClick="() => OnAliasDelete(context)" />
            </MudTd>
        </RowTemplate>
        <RowEditingTemplate>
            <MudTd DataLabel="Alias"><MudTextField @bind-Value="context.Alias" /></MudTd>
            <MudTd DataLabel="ISIN"><MudTextField @bind-Value="context.Isin" /></MudTd>
            <MudTd></MudTd>
        </RowEditingTemplate>
    </MudTable>

    <div class="d-flex gap-2 mt-3" style="align-items:flex-end;">
        <MudTextField @bind-Value="_newAlias" Label="Neuer Alias" Variant="Variant.Outlined" Dense="true" />
        <MudTextField @bind-Value="_newIsin" Label="ISIN" Variant="Variant.Outlined" Dense="true" />
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   Disabled="@(string.IsNullOrWhiteSpace(_newAlias) || string.IsNullOrWhiteSpace(_newIsin))"
                   OnClick="OnAliasAdd">Hinzufügen</MudButton>
    </div>
    @if (_aliasError is not null)
    {
        <MudAlert Severity="Severity.Error" Class="mt-2">@_aliasError</MudAlert>
    }
</SectionCard>
</div>
```

> The `RowEditingTemplate` mutates `context` properties directly; `DividendAliasView` is a record (immutable). To allow in-place edit, either (a) use a small mutable view-model class local to the page, or (b) keep the table read-only with delete + add-row only and drop `RowEditCommit`/`RowEditingTemplate`. Pick (b) for simplicity unless the Basiszins panel already demonstrates editing a mutable row type — then mirror that exactly with a mutable local class `AliasEdit { public string Alias; public string Isin; ... }`.

Add the `@code` members:

```csharp
    private List<WealthIQ.Application.ReferenceData.DividendAliasView> _aliases = new();
    private string _newAlias = "";
    private string _newIsin = "";
    private string? _aliasError;

    private void LoadAliases()
        => _aliases = Db.DividendAliases
            .OrderBy(a => a.Alias)
            .Select(a => new WealthIQ.Application.ReferenceData.DividendAliasView(a.NormalizedAlias, a.Alias, a.Isin))
            .ToList();

    private async Task OnAliasAdd()
    {
        _aliasError = null;
        try
        {
            await AliasService.SetAsync(_newAlias, _newIsin);
            _newAlias = ""; _newIsin = "";
            LoadAliases();
        }
        catch (Exception ex) { _aliasError = ex.Message; }
    }

    private async Task OnAliasDelete(WealthIQ.Application.ReferenceData.DividendAliasView row)
    {
        _aliasError = null;
        try { await AliasService.DeleteAsync(row.NormalizedAlias); LoadAliases(); }
        catch (Exception ex) { _aliasError = ex.Message; }
    }
```

Call `LoadAliases()` inside the page's existing `OnInitialized`/`OnInitializedAsync` (add it next to the existing data loads). If the page has no such method, add:

```csharp
    protected override void OnInitialized() => LoadAliases();
```

(If `OnInitialized`/`OnInitializedAsync` already exists, add the `LoadAliases();` call into it instead of declaring a second one.)

- [ ] **Step 3: Build + manual smoke test note**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeds.

> Per project memory ("Verify Blazor by running"), a manual `dotnet run` smoke test of `/data-admin` and `/import` is required before handoff — build + xUnit don't catch render errors. This is performed in Task 20.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/DataAdmin.razor
git commit -m "feat(web): Stammdaten panel to edit Trader's Place dividend aliases"
```

---

## Phase G — End-to-end regression test + fixtures

### Task 19: Golden fixtures + Trader's Place end-to-end regression test

**Files:**
- Create: `data/test/tradersplace/statements/Depotumsaetze.csv`
- Create: `data/test/tradersplace/statements/Kontoumsaetze.csv`
- Create: `data/test/tradersplace/configuration/instruments.json`
- Create: `data/test/tradersplace/configuration/basiszins.csv`
- Create: `data/test/tradersplace/configuration/fx_rates.csv`
- Create: `data/test/tradersplace/configuration/historical_prices.csv`
- Create: `data/test/tradersplace/configuration/tradersplace_dividend_aliases.csv`
- Create: `tests/WealthIQ.Tests/Application/Tax/TradersPlaceRegressionTests.cs`

- [ ] **Step 1: Copy the sample CSVs into the test fixtures**

Copy the two sample files verbatim (they keep their original Windows-1252 bytes — do NOT re-encode):

```bash
mkdir -p data/test/tradersplace/statements data/test/tradersplace/configuration
cp "data/sample/Depotumsätze_20260606_114607.csv" data/test/tradersplace/statements/Depotumsaetze.csv
cp "data/sample/Kontoumsätze_20260606_114351.csv" data/test/tradersplace/statements/Kontoumsaetze.csv
```

> ASCII filenames avoid cross-platform path issues on the ubuntu CI runner. The byte content (with German encoding) is preserved by `cp`.

- [ ] **Step 2: Create the configuration fixtures**

`data/test/tradersplace/configuration/tradersplace_dividend_aliases.csv` (the regression test resolves both dividend aliases):

```csv
alias,isin
VANGUARD S+P 500U.ETF DLD,IE00B3XXRP09
ISHSIV-DL T.BD20+YR DL D,IE00BSKRJZ44
```

`data/test/tradersplace/configuration/instruments.json` (all ISINs that appear in trades, with profiles so tax replay never fails loud):

```json
{
  "FR0010510800": { "name": "Amundi EUR Overnight Return UCITS ETF Acc", "type": "ETF_MONEY_MARKET", "tfs_quote": 0.00, "subject_to_vorabpauschale": true },
  "FR0010342592": { "name": "Amundi Nasdaq-100 Daily (2x) Leveraged UCITS ETF Acc", "type": "ETF_EQUITY", "tfs_quote": 0.30, "subject_to_vorabpauschale": true },
  "IE00B53SZB19": { "name": "iShares NASDAQ 100 UCITS ETF USD Acc", "type": "ETF_EQUITY", "tfs_quote": 0.30, "subject_to_vorabpauschale": true },
  "IE00B3XXRP09": { "name": "Vanguard S&P 500 UCITS ETF", "type": "ETF_EQUITY", "tfs_quote": 0.30, "subject_to_vorabpauschale": true },
  "IE00BSKRJZ44": { "name": "iShares USD Treasury Bond 20+yr", "type": "ETF_BOND", "tfs_quote": 0.00, "subject_to_vorabpauschale": true },
  "DE000A0S9GB0": { "name": "Xetra-Gold", "type": "ETC", "tfs_quote": 0.00, "subject_to_vorabpauschale": false }
}
```

`data/test/tradersplace/configuration/basiszins.csv` (Basiszins for every year a fund lot is held over year-end: 2024, 2025):

```csv
year,rate
2024,0.0255
2025,0.0253
```

`data/test/tradersplace/configuration/fx_rates.csv` — header only plus one harmless EUR row is not needed because all data is EUR (same-currency conversions return 1.0 without a lookup). Create it with just the header so `CsvFxRateLookup` constructs cleanly:

```csv
date,currency,rate_to_eur
```

> Verify the exact column header `CsvFxRateLookup` expects by reading `src/WealthIQ.Infrastructure/Ibkr/Currency/CsvFxRateLookup.cs`; match it. If it requires at least one row, add a single unused row e.g. `2024-01-01,USD,0.90`.

`data/test/tradersplace/configuration/historical_prices.csv` — year-start (Jan 2) and year-end (Dec 30) bars for every fund instrument held over a year-end, so Vorabpauschale price lookups succeed. Vorabpauschale amounts are **not asserted**, so representative values are fine; only existence matters. Match the 9-column layout the `CsvHistoricalPriceLookup`/seeder expects (`date,provider_symbol,currency,open,high,low,close,adjusted_close,volume`) — confirm exact headers by reading `CsvHistoricalPriceLookup.cs` and the seeder's `ReadHistoricalPrices`. The price lookup keys on **provider symbol**, which is resolved from `listings.json`; to avoid needing listings, confirm whether `DerivedInstrumentPriceProvider` is used (it needs listings) or whether a direct ISIN price source is available. If listings are required, also create `configuration/listings.json` mapping each ISIN to a provider symbol equal to the ISIN, and use the ISIN as `provider_symbol` in the price CSV.

> **Implementer action:** read `tests/WealthIQ.Tests/Application/Tax/GermanTaxRegressionTests.cs` (already in repo) — it wires `DerivedInstrumentPriceProvider(new JsonInstrumentMarketDataMap(listings.json), new CsvHistoricalPriceLookup(historical_prices.csv))`. Mirror that wiring and provide a `listings.json` whose provider_symbol == ISIN, with matching rows in `historical_prices.csv` for: FR0010342592 (2024), IE00B53SZB19 (2024 & 2025), IE00B3XXRP09 (2025), IE00BSKRJZ44 (2025). FR0010510800 is fully sold inside 2024 (not held over a year-end) → no bars needed. Xetra-Gold has `subject_to_vorabpauschale:false` → no bars needed.

- [ ] **Step 3: Write the regression test**

Create `tests/WealthIQ.Tests/Application/Tax/TradersPlaceRegressionTests.cs`. It asserts the **parse** thoroughly and the two clean, price-independent realized sales (no lot held over a year-end → `UsedVorabpauschale == 0`):

```csharp
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Ibkr.MarketData;
using WealthIQ.Infrastructure.Ibkr.Tax;
using WealthIQ.Infrastructure.Ibkr.Currency;
using WealthIQ.Infrastructure.TradersPlace.Import;
using Xunit;

namespace WealthIQ.Tests.Application.Tax;

public sealed class TradersPlaceRegressionTests
{
    private sealed class CsvAliasMap : IDividendAliasMap
    {
        private readonly Dictionary<string, string> _map = new();
        public CsvAliasMap(string path)
        {
            foreach (var line in File.ReadLines(path).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                _map[DividendAliasNormalizer.Normalize(parts[0])] = parts[1].Trim();
            }
        }
        public string? ResolveIsin(string alias)
            => _map.TryGetValue(DividendAliasNormalizer.Normalize(alias), out var i) ? i : null;
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WealthIQ.slnx")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public async Task Import_BothCsvs_ParsesTradesCashAndKestUnderOneAccount()
    {
        var root = Root();
        var statements = Path.Combine(root, "data", "test", "tradersplace", "statements");
        var config = Path.Combine(root, "data", "test", "tradersplace", "configuration");

        var importer = new TradersPlaceStatementImporter(
            new CsvAliasMap(Path.Combine(config, "tradersplace_dividend_aliases.csv")));

        var accountId = (AccountId)Guid.Parse("33333333-3333-3333-3333-333333333333");
        var importResult = await importer.ImportAsync(new ImportRequest
        {
            AccountId = accountId,
            Source = new ImportSource(Broker.TradersPlace, Format.CSV, statements)
        }, CancellationToken.None);

        Assert.DoesNotContain(importResult.Diagnostics, d => d.Severity >= ImportDiagnosticSeverity.Error);

        var trades = importResult.PortfolioLedger.Entries.OfType<TradeEntry>().ToList();
        var cash = importResult.PortfolioLedger.Entries.OfType<CashEntry>().ToList();

        Assert.Equal(16, trades.Count);  // 12 Kauf + 4 Verkauf from Depotumsätze
        Assert.Equal(6, cash.Count(c => c.CashFlowType == CashFlowType.Dividend));   // 6 Effekten
        Assert.Equal(3, cash.Count(c => c.CashFlowType == CashFlowType.Interest));   // 3 positive Kontoabschluss
        Assert.All(importResult.PortfolioLedger.Entries, e => Assert.Equal(accountId, e.AccountId));

        // KESt only on the Vanguard 2025 sale (340.29).
        Assert.Equal(340.29m, trades.Where(t => t.Side == TradeSide.Sell).Sum(t => t.WithheldTax.Amount));
    }

    [Fact]
    public async Task Calculate_CleanWithinYearSales_MatchRawAndTaxableAndKest()
    {
        var root = Root();
        var statements = Path.Combine(root, "data", "test", "tradersplace", "statements");
        var config = Path.Combine(root, "data", "test", "tradersplace", "configuration");

        var importer = new TradersPlaceStatementImporter(
            new CsvAliasMap(Path.Combine(config, "tradersplace_dividend_aliases.csv")));
        var importResult = await importer.ImportAsync(new ImportRequest
        {
            AccountId = (AccountId)Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Source = new ImportSource(Broker.TradersPlace, Format.CSV, statements)
        }, CancellationToken.None);

        var catalog = new InstrumentCatalogBuilder(
            new JsonInstrumentProfileEnricher(Path.Combine(config, "instruments.json")))
            .Build(importResult.Instruments);

        var priceProvider = new DerivedInstrumentPriceProvider(
            new JsonInstrumentMarketDataMap(Path.Combine(config, "listings.json")),
            new CsvHistoricalPriceLookup(Path.Combine(config, "historical_prices.csv")));

        var calculator = new GermanTaxCalculator(
            new CsvBasisInterestRateProvider(Path.Combine(config, "basiszins.csv")),
            priceProvider,
            new CsvFxRateLookup(Path.Combine(config, "fx_rates.csv")));

        var result = calculator.Calculate(importResult.PortfolioLedger, catalog);

        var sells = result.Entries.Where(e => e.Type == GermanTaxEntryType.Sell).ToList();

        // Amundi EUR Overnight (FR0010510800): bought 100+361+369 @108.259/108.278, sold 830 @108.510
        // within 2024 (not held over a year-end) → usedVorab = 0. Money-market TFS = 0 → taxable == raw.
        var amundiSells = sells.Where(s => s.Isin == "FR0010510800").ToList();
        Assert.Equal(201.32m, decimal.Round(amundiSells.Sum(s => s.RawAmount), 2));
        Assert.Equal(0m, amundiSells.Sum(s => s.UsedVorabpauschale));

        // Vanguard (IE00B3XXRP09) 2025 sale: 581 @112.895 from the 835 @97.888 lot, both within 2025
        // (not held over a year-end) → usedVorab = 0. raw = 581*(112.895-97.888) = 8719.07.
        var vanguardSell = Assert.Single(sells.Where(s => s.Isin == "IE00B3XXRP09"));
        Assert.Equal(8719.07m, decimal.Round(vanguardSell.RawAmount, 2));
        Assert.Equal(0m, vanguardSell.UsedVorabpauschale);
        Assert.Equal(6103.35m, decimal.Round(vanguardSell.TaxableAmount, 2)); // raw * (1 - 0.30)
        Assert.Equal(340.29m, vanguardSell.WithheldKESt);
    }
}
```

> If the Vanguard FIFO produces more than one slice (it shouldn't: 581 ≤ the first 835-share lot), `Assert.Single` will fail — in that case sum the IE00B3XXRP09 sells instead. Confirm against the data.

- [ ] **Step 4: Run the regression tests**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TradersPlaceRegressionTests"`
Expected: PASS (2 tests). If the calc throws "year-start/year-end price missing", add the missing bar(s) to `historical_prices.csv`/`listings.json` for the named ISIN/year and re-run (Vorabpauschale values are not asserted, so any positive price works).

- [ ] **Step 5: Commit**

```bash
git add data/test/tradersplace/ tests/WealthIQ.Tests/Application/Tax/TradersPlaceRegressionTests.cs
git commit -m "test(tradersplace): golden fixtures + end-to-end import/tax regression"
```

---

## Phase H — Full verification

### Task 20: Whole-suite + format + manual smoke test

- [ ] **Step 1: Format check**

Run: `dotnet format WealthIQ.slnx --verify-no-changes`
Expected: no changes. If it reports changes, run `dotnet format WealthIQ.slnx` and commit them.

- [ ] **Step 2: Full Release build + test (matches CI)**

Run:

```bash
dotnet build WealthIQ.slnx --configuration Release
dotnet test WealthIQ.slnx --configuration Release --no-build
```

Expected: Build succeeds, all tests pass (including the existing `GermanTaxRegressionTests`).

- [ ] **Step 3: Manual Blazor smoke test (required by project memory)**

Run: `dotnet run --project src/WealthIQ.Web` and in a browser:
- `/import`: switch broker to Trader's Place, upload both sample CSVs under an account number, confirm the import commits (no blocking diagnostics).
- `/` (Steuerreport): confirm the account dropdown appears (with the new account), KESt KPI shows, year switching works.
- `/data-admin`: confirm the dividend-alias panel lists the seeded aliases and add/delete works.
- `/browse/ledger`: confirm the Trader's Place entries appear under the new account.

Expected: all pages render without errors; data is scoped to the correct account.

- [ ] **Step 4: Update CLAUDE.md**

Add Trader's Place to the documented brokers/importers and note the per-account Steuerreport and dividend-alias Stammdaten panel. Keep edits scoped. Then commit:

```bash
git add CLAUDE.md
git commit -m "docs: document Trader's Place import + per-account tax report"
```

---

## Self-review (completed by plan author)

**Spec coverage:**
- §2–§5 two-file import, header routing, dedup, FX, plumbing → Tasks 10–13, 16. ✅
- §6 dividend alias map (table, seed, fail-loud, UI) → Tasks 5–9, 18. ✅
- §7 KESt as prepaid tax → Tasks 1, 3, 4 (TradeEntry.WithheldTax, allocation, summary), 17 (display). ✅
- §8 per-account report → Tasks 2, 3, 4, 17. ✅
- §9 reference/test data → Tasks 8, 14, 19. ✅
- §10 testing → tests in every task + Task 19 e2e. ✅
- §11 Xetra-Gold deferred → Task 14 (profile with `subject_to_vorabpauschale:false`) + comment; documented as deferred (no tax-free logic added). ✅
- §12 touched components → all covered.

**Type consistency:** `WithheldTax` (TradeEntry), `WithheldKESt` (GermanTaxEntry/TaxReportSummary), `AccountTaxReport(AccountId, AccountNumber, Years)`, `IDividendAliasMap.ResolveIsin`, `IDividendAliasStore.Upsert/Delete/SaveChangesAsync`, `DividendAliasRefreshService.SetAsync/DeleteAsync`, `IRawFileStore.IngestDirectory`, `StatementImportPipeline(IEnumerable<IStatementImporter>, ...)` — used consistently across tasks.

**Known verification points flagged inline** (read-before-code): `ImportDiagnosticCode` member names, `ImportDiagnostic` ctor params, `IImportStore`/`ImportPersistCounts` signatures, `CsvFxRateLookup`/`CsvHistoricalPriceLookup` headers + `listings.json` wiring, and whether the Basiszins panel edits a mutable row type. These are existing-code details the implementer confirms against the repo; the plan gives the exact file to read for each.
