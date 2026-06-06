# Phase 3 Streamlining Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Streamline the Phase 3 UI — account-scoped ledger with delete, FX incremental/add-currency, simplified Basiszins, Kurschart preselect/remember/zoom, and Steuerreport in-place source/explanation expanders — per `docs/superpowers/specs/2026-06-06-phase3-streamlining-design.md`.

**Architecture:** Mostly Blazor Server UI changes. Two backend changes: (a) FX provider gains a currency filter and the refresh service gains incremental + add-currency methods; (b) `GermanTaxEntry` gains additive display-only fields populated by `GermanTaxCalculator` so the report can show origin/calculation inline. No tax math changes — the golden regression baseline stays green unchanged.

**Tech Stack:** C# / .NET 10, Blazor Server + MudBlazor, EF Core + SQLite, TradingView Lightweight Charts v4, xUnit.

**Conventions:** `decimal` for money/quantities; nullable enabled; one public type per file; English identifiers; 4-space indent. Build: `dotnet build WealthIQ.slnx`. Tests: `dotnet test WealthIQ.slnx`. Format: `dotnet format WealthIQ.slnx`. Commit messages follow the repo style (`Feat:`, `Refine:`, `Docs:` prefixes).

---

## File overview

**Backend (item 3 — FX):**
- Modify `src/WealthIQ.Application/Currency/Interface/IFxRateProvider.cs` — add currency filter param.
- Modify `src/WealthIQ.Infrastructure/Ibkr/Currency/EcbFxRateProvider.cs` — honor the filter.
- Modify `src/WealthIQ.Application/Currency/FxRateRefreshModels.cs` — add store methods to `IFxRateStore`.
- Modify `src/WealthIQ.Infrastructure/ReferenceData/DbFxRateStore.cs` — implement the new store methods.
- Modify `src/WealthIQ.Application/Currency/FxRateRefreshService.cs` — add `RefreshIncrementalAsync`, `AddCurrencyAsync`.
- Modify tests `tests/WealthIQ.Tests/Application/Currency/FxRateRefreshServiceTests.cs`, `tests/WealthIQ.Tests/Infrastructure/Currency/EcbFxRateProviderTests.cs`.

**Backend (item 9 — tax detail):**
- Modify `src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs` — add optional display fields.
- Modify `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs` — populate them.
- Modify test `tests/WealthIQ.Tests/Application/Tax/GermanTaxEntryDetailTests.cs`.

**Web (items 1, 2, 4–10):**
- Create `src/WealthIQ.Web/Services/ChartSelectionState.cs` — scoped selection memory.
- Modify `src/WealthIQ.Web/Program.cs` — register `ChartSelectionState`.
- Modify `src/WealthIQ.Web/wwwroot/wiq-charts.js` — last-year initial zoom.
- Modify `src/WealthIQ.Web/wwwroot/wealthiq.js` — scroll-and-highlight helper.
- Modify `src/WealthIQ.Web/wwwroot/wealthiq.css` — row-highlight animation.
- Modify `src/WealthIQ.Web/Components/Shared/LightweightChart.razor` — pass `InitialRangeDays`.
- Modify `src/WealthIQ.Web/Components/Pages/Browse/PriceChart.razor` — preselect/remember/no-clear (items 4, 5).
- Modify `src/WealthIQ.Web/Components/Pages/Browse/LedgerBrowser.razor` — account select + delete (items 1, 2).
- Modify `src/WealthIQ.Web/Components/Pages/DataAdmin.razor` — remove panels, FX UI, Basiszins streamline (items 2, 3, 6, 7).
- Modify `src/WealthIQ.Web/Components/Pages/Steuerreport.razor` — highlight + inline expand + column (items 8, 9, 10).

---

## Task 1: FX provider currency filter

**Files:**
- Modify: `src/WealthIQ.Application/Currency/Interface/IFxRateProvider.cs`
- Modify: `src/WealthIQ.Infrastructure/Ibkr/Currency/EcbFxRateProvider.cs:14-51`
- Modify (compile fixes): `tests/WealthIQ.Tests/Application/Currency/FxRateRefreshServiceTests.cs:9-12`, `tests/WealthIQ.Tests/Infrastructure/Currency/EcbFxRateProviderTests.cs`

- [x] **Step 1: Update the provider interface**

Replace the contents of `IFxRateProvider.cs`:

```csharp
namespace WealthIQ.Application.Currency.Interface;

public interface IFxRateProvider
{
    /// <summary>Fetches FX rates in [from, to]. When <paramref name="currencies"/> is non-null, only
    /// those currency codes are returned (EUR base is always implied); when null the provider's
    /// configured default set is used.</summary>
    Task<IReadOnlyList<FxRateRecord>> FetchAsync(
        DateOnly from, DateOnly to, IReadOnlyCollection<string>? currencies, CancellationToken ct);
}
```

- [x] **Step 2: Update EcbFxRateProvider to honor the filter**

In `EcbFxRateProvider.cs`, change the method signature and the `supported` set source:

```csharp
    public async Task<IReadOnlyList<FxRateRecord>> FetchAsync(
        DateOnly from, DateOnly to, IReadOnlyCollection<string>? currencies, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.HistoricalUrl);
        request.Headers.UserAgent.ParseAdd(options.UserAgent);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(ct);

        var supported = (currencies is { Count: > 0 } ? currencies : options.SupportedCurrencies)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
```

Leave the rest of the method body unchanged (it already filters via `supported.Contains(currency)` and always emits the `EUR=1m` row).

- [x] **Step 3: Fix the existing fake + provider test to compile**

In `FxRateRefreshServiceTests.cs`, update `FakeProvider.FetchAsync`:

```csharp
        public Task<IReadOnlyList<FxRateRecord>> FetchAsync(
            DateOnly from, DateOnly to, IReadOnlyCollection<string>? currencies, CancellationToken ct)
            => Task.FromResult(records);
```

In `EcbFxRateProviderTests.cs`, update every `FetchAsync(from, to, ...)` call site to pass the new `currencies` argument as `null` (preserving existing behavior). Add one new test verifying the filter:

```csharp
    [Fact]
    public async Task FetchAsync_WithCurrencyFilter_ReturnsOnlyRequestedPlusEur()
    {
        // Arrange: reuse this test class's existing sample-XML HttpClient setup (see other tests
        // in this file) to construct the provider, then request only "GBP".
        var provider = CreateProviderWithSampleXml(); // mirror the helper already used here
        var rows = await provider.FetchAsync(new DateOnly(1999, 1, 1), new DateOnly(2099, 1, 1),
            new[] { "GBP" }, CancellationToken.None);

        Assert.Contains(rows, r => r.Currency == "GBP");
        Assert.DoesNotContain(rows, r => r.Currency == "USD");
        Assert.Contains(rows, r => r.Currency == "EUR"); // base always emitted
    }
```

> If `EcbFxRateProviderTests` does not already expose a reusable sample-XML helper, inline the same `HttpClient`/sample-XML construction the file's other tests use rather than inventing a new fixture.

- [x] **Step 4: Build and run the touched tests**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~EcbFxRateProvider|FullyQualifiedName~FxRateRefreshService"`
Expected: PASS (including the new filter test).

- [x] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/Currency/Interface/IFxRateProvider.cs src/WealthIQ.Infrastructure/Ibkr/Currency/EcbFxRateProvider.cs tests/WealthIQ.Tests/Application/Currency/FxRateRefreshServiceTests.cs tests/WealthIQ.Tests/Infrastructure/Currency/EcbFxRateProviderTests.cs
git commit -m "Feat: FX provider accepts a currency filter"
```

---

## Task 2: FX store — tracked currencies + max stored date

**Files:**
- Modify: `src/WealthIQ.Application/Currency/FxRateRefreshModels.cs`
- Modify: `src/WealthIQ.Infrastructure/ReferenceData/DbFxRateStore.cs`
- Modify (compile fix): `tests/WealthIQ.Tests/Application/Currency/FxRateRefreshServiceTests.cs:14-19`

- [x] **Step 1: Extend the store interface**

Replace `FxRateRefreshModels.cs` contents:

```csharp
namespace WealthIQ.Application.Currency;

public interface IFxRateStore
{
    /// <summary>Upsert FX rate records by (Date, Currency). Returns (added, updated).</summary>
    (int Added, int Updated) Upsert(IReadOnlyList<FxRateRecord> records);

    /// <summary>Currencies that already have stored rows (distinct), excluding the EUR base.</summary>
    IReadOnlyList<string> GetStoredCurrencies();

    /// <summary>Latest stored date across all currencies, or null if the table is empty.</summary>
    DateOnly? GetMaxStoredDate();

    Task SaveChangesAsync(CancellationToken ct);
}
```

- [x] **Step 2: Implement in DbFxRateStore**

Add to `DbFxRateStore.cs` (before `SaveChangesAsync`):

```csharp
    public IReadOnlyList<string> GetStoredCurrencies() =>
        db.FxRates.Select(x => x.Currency)
            .Where(c => c != "EUR")
            .Distinct()
            .OrderBy(c => c)
            .ToList();

    public DateOnly? GetMaxStoredDate() =>
        db.FxRates.Any() ? db.FxRates.Max(x => x.Date) : (DateOnly?)null;
```

- [x] **Step 3: Update the FakeStore in tests**

In `FxRateRefreshServiceTests.cs`, extend `FakeStore`:

```csharp
    private sealed class FakeStore : IFxRateStore
    {
        public List<FxRateRecord> Saved = new();
        public List<string> Stored = new();
        public DateOnly? MaxDate;
        public (int, int) Upsert(IReadOnlyList<FxRateRecord> records) { Saved.AddRange(records); return (records.Count, 0); }
        public IReadOnlyList<string> GetStoredCurrencies() => Stored;
        public DateOnly? GetMaxStoredDate() => MaxDate;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
```

- [x] **Step 4: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: build succeeds (test project compiles with the extended fake).

- [x] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/Currency/FxRateRefreshModels.cs src/WealthIQ.Infrastructure/ReferenceData/DbFxRateStore.cs tests/WealthIQ.Tests/Application/Currency/FxRateRefreshServiceTests.cs
git commit -m "Feat: FX store exposes stored currencies and max date"
```

---

## Task 3: FX refresh service — incremental + add currency

**Files:**
- Modify: `src/WealthIQ.Application/Currency/FxRateRefreshService.cs`
- Test: `tests/WealthIQ.Tests/Application/Currency/FxRateRefreshServiceTests.cs`

- [x] **Step 1: Write failing tests**

Add to `FxRateRefreshServiceTests.cs`. The fake provider currently ignores its params; add a capturing fake so we can assert the requested window/currencies:

```csharp
    private sealed class CapturingProvider : IFxRateProvider
    {
        public DateOnly? From; public DateOnly? To; public IReadOnlyCollection<string>? Currencies;
        private readonly IReadOnlyList<FxRateRecord> _records;
        public CapturingProvider(IReadOnlyList<FxRateRecord> records) => _records = records;
        public Task<IReadOnlyList<FxRateRecord>> FetchAsync(
            DateOnly from, DateOnly to, IReadOnlyCollection<string>? currencies, CancellationToken ct)
        { From = from; To = to; Currencies = currencies; return Task.FromResult(_records); }
    }

    [Fact]
    public async Task RefreshIncrementalAsync_FetchesFromDayAfterMaxStoredDate()
    {
        var provider = new CapturingProvider([new FxRateRecord(new DateOnly(2025, 1, 2), "USD", 0.9m)]);
        var store = new FakeStore { MaxDate = new DateOnly(2025, 1, 1), Stored = { "USD", "GBP" } };

        var service = new FxRateRefreshService(provider, store);
        var result = await service.RefreshIncrementalAsync(new DateOnly(2025, 1, 31), CancellationToken.None);

        Assert.Equal(new DateOnly(2025, 1, 2), provider.From);
        Assert.Equal(new DateOnly(2025, 1, 31), provider.To);
        Assert.Contains("USD", provider.Currencies!);
        Assert.Contains("GBP", provider.Currencies!);
        Assert.Equal(1, result.Added);
    }

    [Fact]
    public async Task AddCurrencyAsync_FetchesOnlyThatCurrency()
    {
        var provider = new CapturingProvider([new FxRateRecord(new DateOnly(2024, 6, 1), "JPY", 0.006m)]);
        var store = new FakeStore();

        var service = new FxRateRefreshService(provider, store);
        var result = await service.AddCurrencyAsync("JPY", new DateOnly(2020, 1, 1), new DateOnly(2024, 12, 31), CancellationToken.None);

        Assert.Equal(new[] { "JPY" }, provider.Currencies);
        Assert.Equal(1, result.Added);
        Assert.Single(store.Saved);
    }
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~FxRateRefreshService"`
Expected: FAIL — `RefreshIncrementalAsync`/`AddCurrencyAsync` do not exist.

- [x] **Step 3: Implement the new methods**

Replace `FxRateRefreshService.cs` with:

```csharp
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.ReferenceData;

namespace WealthIQ.Application.Currency;

/// <summary>Fetches FX rates via the provider, upserts into the store by (Date, Currency).</summary>
public sealed class FxRateRefreshService(IFxRateProvider provider, IFxRateStore store)
{
    private static readonly string[] DefaultCurrencies = ["USD", "GBP", "CHF"];

    /// <summary>Explicit [from, to] refresh of the provider's default currency set.</summary>
    public async Task<DataRefreshResult> RefreshAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var records = await provider.FetchAsync(from, to, null, ct);
        var (added, updated) = store.Upsert(records);
        await store.SaveChangesAsync(ct);
        return new DataRefreshResult(added, updated, 0, []);
    }

    /// <summary>Incremental refresh of every currently-tracked currency
    /// (stored currencies ∪ defaults) from the day after the latest stored date through asOf.</summary>
    public async Task<DataRefreshResult> RefreshIncrementalAsync(DateOnly asOf, CancellationToken ct)
    {
        var tracked = store.GetStoredCurrencies()
            .Concat(DefaultCurrencies)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var from = store.GetMaxStoredDate()?.AddDays(1) ?? asOf.AddYears(-5);
        if (from > asOf)
        {
            return new DataRefreshResult(0, 0, 1, []);
        }

        var records = await provider.FetchAsync(from, asOf, tracked, ct);
        var (added, updated) = store.Upsert(records);
        await store.SaveChangesAsync(ct);
        return new DataRefreshResult(added, updated, 0, []);
    }

    /// <summary>Backfills a single currency over [from, to]. Once stored it becomes part of the
    /// tracked set picked up by <see cref="RefreshIncrementalAsync"/>.</summary>
    public async Task<DataRefreshResult> AddCurrencyAsync(string currency, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var records = await provider.FetchAsync(from, to, new[] { currency }, ct);
        var (added, updated) = store.Upsert(records);
        await store.SaveChangesAsync(ct);
        return new DataRefreshResult(added, updated, 0, []);
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~FxRateRefreshService"`
Expected: PASS (all FX refresh tests, old + new).

- [x] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/Currency/FxRateRefreshService.cs tests/WealthIQ.Tests/Application/Currency/FxRateRefreshServiceTests.cs
git commit -m "Feat: FX incremental refresh and add-currency backfill"
```

---

## Task 4: GermanTaxEntry detail fields + calculator population

**Files:**
- Modify: `src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs`
- Modify: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs` (sell ~115, dividend ~154, interest ~184, withholding ~206, Vorab ~311)
- Test: `tests/WealthIQ.Tests/Application/Tax/GermanTaxEntryDetailTests.cs`

- [x] **Step 1: Add the fields to GermanTaxEntry**

Append optional params to the record struct (after `Origin`):

```csharp
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
    // --- Display-only source/explanation fields (item 9). Not used by tax math. ---
    // Cash + sell: broker references and source file for the in-place "Quelle" expander.
    string SourceReference = "",     // cash: txn ref; sell: OPEN trade ref
    string CloseReference = "",      // sell: CLOSE trade ref (empty for cash)
    string SourceFile = "",          // originating statement file
    decimal OriginalAmount = 0m,     // cash: gross amount in original currency
    string OriginalCurrency = "",    // cash: original currency code
    // Vorabpauschale: the §18 calculation inputs for the "warum?" expander.
    decimal YearStartPrice = 0m,     // year-start redemption price, EUR
    decimal YearEndPrice = 0m,       // year-end redemption price, EUR
    decimal BasisRate = 0m,          // Basiszins used
    decimal HeldQuantity = 0m,       // shares held in the lot
    decimal DistributionPerShare = 0m,
    decimal MonthFactor = 0m);
```

- [x] **Step 2: Write failing tests for population**

Add to `GermanTaxEntryDetailTests.cs` (reuse the existing import-based setup; factor the repeated arrange into a private helper `BuildResult()` returning `result.Entries`, or copy the arrange block as the other tests do):

```csharp
    [Fact]
    public async Task Calculate_DividendEntries_CarrySourceReferenceAndOriginalAmount()
    {
        var entries = await BuildEntriesAsync();
        var dividends = entries.Where(x => x.Type == GermanTaxEntryType.Dividend).ToList();

        Assert.NotEmpty(dividends);
        Assert.All(dividends, d => Assert.False(string.IsNullOrWhiteSpace(d.SourceReference), $"{d.Symbol} dividend missing SourceReference"));
        Assert.All(dividends, d => Assert.False(string.IsNullOrWhiteSpace(d.OriginalCurrency), $"{d.Symbol} dividend missing OriginalCurrency"));
    }

    [Fact]
    public async Task Calculate_SellEntries_CarryOpenAndCloseReferences()
    {
        var entries = await BuildEntriesAsync();
        var sells = entries.Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Sell).ToList();

        Assert.NotEmpty(sells);
        Assert.All(sells, s => Assert.False(string.IsNullOrWhiteSpace(s.SourceReference), $"{s.Symbol} sell missing open ref"));
        Assert.All(sells, s => Assert.False(string.IsNullOrWhiteSpace(s.CloseReference), $"{s.Symbol} sell missing close ref"));
    }

    [Fact]
    public async Task Calculate_VorabpauschaleEntries_CarryCalculationInputs()
    {
        var entries = await BuildEntriesAsync();
        var vorab = entries.Where(x => x.Type == GermanTaxEntryType.Vorabpauschale).ToList();

        Assert.NotEmpty(vorab);
        Assert.All(vorab, v => Assert.True(v.YearStartPrice > 0m, $"{v.Symbol} vorab missing YearStartPrice"));
        Assert.All(vorab, v => Assert.True(v.BasisRate > 0m, $"{v.Symbol} vorab missing BasisRate"));
        Assert.All(vorab, v => Assert.True(v.HeldQuantity > 0m, $"{v.Symbol} vorab missing HeldQuantity"));
    }
```

Add the shared helper (extract the existing arrange block verbatim into it):

```csharp
    private static async Task<IReadOnlyList<GermanTaxEntry>> BuildEntriesAsync()
    {
        var repoRoot = FindRepositoryRoot();
        var inputPath = Path.Combine(repoRoot, "data", "test", "statements");
        var configurationPath = Path.Combine(repoRoot, "data", "test", "configuration");

        var importer = new IbkrStatementImporter();
        var importResult = await importer.ImportAsync(new ImportRequest
        {
            AccountId = (AccountId)Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, inputPath)
        }, CancellationToken.None);

        var instrumentCatalog = new InstrumentCatalogBuilder(
            new JsonInstrumentProfileEnricher(Path.Combine(configurationPath, "instruments.json")))
            .Build(importResult.Instruments);

        var priceProvider = new DerivedInstrumentPriceProvider(
            new JsonInstrumentMarketDataMap(Path.Combine(configurationPath, "listings.json")),
            new CsvHistoricalPriceLookup(Path.Combine(configurationPath, "historical_prices.csv")));

        var calculator = new GermanTaxCalculator(
            new CsvBasisInterestRateProvider(Path.Combine(configurationPath, "basiszins.csv")),
            priceProvider,
            new CsvFxRateLookup(Path.Combine(configurationPath, "fx_rates.csv")));

        return calculator.Calculate(importResult.PortfolioLedger, instrumentCatalog).Entries;
    }
```

- [x] **Step 3: Run tests to verify they fail**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxEntryDetail"`
Expected: FAIL — new fields are still default (empty/zero).

- [x] **Step 4: Populate the fields in GermanTaxCalculator**

Sell site (the `new GermanTaxEntry(` at ~line 115) — add named args before the closing paren:

```csharp
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
                SourceFile: tradeEntry.SourceProvenance.SourceLocation));
```

Dividend site (~154):

```csharp
                ledger.Add(new GermanTaxEntry(
                    cashEntry.OccurredAt.Year,
                    date,
                    GermanTaxEntryType.Dividend,
                    dividendInstrument.Symbol,
                    dividendInstrument.ISIN,
                    rawDividend,
                    rawDividend * (1m - dividendInstrument.Teilfreistellungsquote),
                    SourceReference: cashEntry.SourceProvenance.SourceRecordReference,
                    SourceFile: cashEntry.SourceProvenance.SourceLocation,
                    OriginalAmount: cashEntry.GrossAmount.Amount,
                    OriginalCurrency: cashEntry.GrossAmount.Currency.ToString()));
```

Interest site (~184):

```csharp
                ledger.Add(new GermanTaxEntry(
                    cashEntry.OccurredAt.Year,
                    date,
                    GermanTaxEntryType.Interest,
                    interestInstrument.Symbol,
                    interestInstrument.ISIN,
                    _fxConverter.Convert(cashEntry.GrossAmount, date).Amount,
                    _fxConverter.Convert(cashEntry.GrossAmount, date).Amount,
                    SourceReference: cashEntry.SourceProvenance.SourceRecordReference,
                    SourceFile: cashEntry.SourceProvenance.SourceLocation,
                    OriginalAmount: cashEntry.GrossAmount.Amount,
                    OriginalCurrency: cashEntry.GrossAmount.Currency.ToString()));
```

Withholding site (~206) — keep `Origin`, add the source fields:

```csharp
                ledger.Add(new GermanTaxEntry(
                    cashEntry.OccurredAt.Year,
                    date,
                    GermanTaxEntryType.WithholdingTax,
                    withholdingInstrument.Symbol,
                    withholdingInstrument.ISIN,
                    withholdingTaxAmount,
                    0m,
                    ForeignWithholdingTax: Math.Abs(withholdingTaxAmount),
                    Origin: withholdingOrigin,
                    SourceReference: cashEntry.SourceProvenance.SourceRecordReference,
                    SourceFile: cashEntry.SourceProvenance.SourceLocation,
                    OriginalAmount: cashEntry.GrossAmount.Amount,
                    OriginalCurrency: cashEntry.GrossAmount.Currency.ToString()));
```

Vorabpauschale site (~311) — the inputs (`startValueEur`, `endValueEur`, `basisInterestRate`, `distributionPerShare`, `monthFactor`, `lot.RemainingQuantity`) are all in scope at this point:

```csharp
                ledger.Add(new GermanTaxEntry(
                    year + 1,
                    new DateOnly(year + 1, 1, 1),
                    GermanTaxEntryType.Vorabpauschale,
                    instrument.Symbol,
                    instrument.ISIN,
                    totalVorabpauschale,
                    totalVorabpauschale * (1m - instrument.Teilfreistellungsquote),
                    YearStartPrice: startValueEur,
                    YearEndPrice: endValueEur,
                    BasisRate: basisInterestRate.Value,
                    HeldQuantity: lot.RemainingQuantity.Value,
                    DistributionPerShare: distributionPerShare,
                    MonthFactor: monthFactor));
```

- [x] **Step 5: Run the detail tests + the regression baseline**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxEntryDetail|FullyQualifiedName~GermanTaxRegression"`
Expected: PASS — detail fields populated; regression figures unchanged (additive fields don't affect computed amounts).

- [x] **Step 6: Commit**

```bash
git add src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs src/WealthIQ.Application/Tax/GermanTaxCalculator.cs tests/WealthIQ.Tests/Application/Tax/GermanTaxEntryDetailTests.cs
git commit -m "Feat: GermanTaxEntry carries source/explanation detail for the report"
```

---

## Task 5: ChartSelectionState scoped service

**Files:**
- Create: `src/WealthIQ.Web/Services/ChartSelectionState.cs`
- Modify: `src/WealthIQ.Web/Program.cs:61` (near the other `AddScoped` UI services)

- [x] **Step 1: Create the service**

```csharp
namespace WealthIQ.Web.Services;

/// <summary>Per-circuit memory of the user's chart selections so navigating away and back
/// (within the same Blazor Server session) restores the last-viewed instrument. Resets on full reload.</summary>
public sealed class ChartSelectionState
{
    /// <summary>Selected Kurschart provider symbol, or null if none chosen yet.</summary>
    public string? SelectedPriceSymbol { get; set; }
}
```

- [x] **Step 2: Register it in DI**

In `Program.cs`, next to `builder.Services.AddScoped<WealthIQ.Web.Services.ThemePreferenceService>();`, add:

```csharp
builder.Services.AddScoped<WealthIQ.Web.Services.ChartSelectionState>();
```

- [x] **Step 3: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: build succeeds.

- [x] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Services/ChartSelectionState.cs src/WealthIQ.Web/Program.cs
git commit -m "Feat: scoped ChartSelectionState for remembering chart selection"
```

---

## Task 6: Chart last-year initial zoom (JS + component)

**Files:**
- Modify: `src/WealthIQ.Web/wwwroot/wiq-charts.js:32-37`
- Modify: `src/WealthIQ.Web/Components/Shared/LightweightChart.razor`

- [x] **Step 1: Add an initial-range option to the JS `setData`**

Replace the `setData` function in `wiq-charts.js` with a version that accepts an optional `initialRangeDays`:

```javascript
    setData: function (id, data, initialRangeDays) {
        var entry = this._charts[id];
        if (!entry) return;
        var points = data || [];
        entry.series.setData(points);

        // When an initial window is requested and there is enough data, show only the last
        // `initialRangeDays` days; otherwise fit everything. Times are "yyyy-MM-dd" strings.
        if (initialRangeDays && points.length > 0) {
            var lastTime = points[points.length - 1].time;
            var last = new Date(lastTime + 'T00:00:00Z');
            var firstAvailable = new Date(points[0].time + 'T00:00:00Z');
            var from = new Date(last);
            from.setUTCDate(from.getUTCDate() - initialRangeDays);
            if (from <= firstAvailable) {
                entry.chart.timeScale().fitContent();
            } else {
                var iso = function (d) { return d.toISOString().slice(0, 10); };
                entry.chart.timeScale().setVisibleRange({ from: iso(from), to: lastTime });
            }
        } else {
            entry.chart.timeScale().fitContent();
        }
    },
```

- [x] **Step 2: Pass an `InitialRangeDays` parameter from the component**

In `LightweightChart.razor`, add the parameter and forward it in `PushDataAsync`:

```csharp
    [Parameter] public string Height { get; set; } = "460px";

    /// <summary>When set, the chart opens showing only the last N days of data (else fits all).</summary>
    [Parameter] public int? InitialRangeDays { get; set; }
```

Update both `setData` interop calls in `PushDataAsync` to pass `InitialRangeDays`:

```csharp
    private async Task PushDataAsync()
    {
        if (Kind == "line")
        {
            var data = (Line ?? Array.Empty<LinePoint>())
                .Select(p => new { time = p.Time, value = p.Value });
            await JS.InvokeVoidAsync("wiqCharts.setData", _id, data, InitialRangeDays);
        }
        else
        {
            var data = (Candles ?? Array.Empty<Candle>())
                .Select(c => new { time = c.Time, open = c.Open, high = c.High, low = c.Low, close = c.Close });
            await JS.InvokeVoidAsync("wiqCharts.setData", _id, data, InitialRangeDays);
        }
    }
```

> Note: `InitialRangeDays` null serializes fine to JS (`undefined`-like) and the JS guard `if (initialRangeDays ...)` treats it as "fit all", so the FX line chart (which doesn't set it) is unaffected.

- [x] **Step 3: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: build succeeds. (JS verified in the Task 11 smoke test.)

- [x] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/wwwroot/wiq-charts.js src/WealthIQ.Web/Components/Shared/LightweightChart.razor
git commit -m "Feat: chart supports a last-N-days initial visible range"
```

---

## Task 7: Kurschart — no-clear, preselect, remember, zoom (items 4 & 5)

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Browse/PriceChart.razor`

- [x] **Step 1: Inject the selection state and set the chart range**

At the top of `PriceChart.razor`, after the existing `@inject` lines, add:

```razor
@inject WealthIQ.Web.Services.ChartSelectionState ChartSelection
```

Change the autocomplete to remove the clear button (set `Clearable="false"`):

```razor
        <MudAutocomplete T="ListingOption" Value="_selected" ValueChanged="OnSymbolChanged"
                         SearchFunc="SearchListings" ToStringFunc="o => o is null ? string.Empty : o.Label"
                         Label="Instrument" Variant="Variant.Outlined" Dense="true"
                         Clearable="false" Style="min-width:320px;" />
```

Pass the initial range to the chart (item 5):

```razor
                <LightweightChart Kind="candlestick" Candles="_candles" Dark="_dark" InitialRangeDays="365" />
```

- [x] **Step 2: Preselect + restore in OnInitializedAsync**

Replace `OnInitializedAsync` in `PriceChart.razor` with logic that restores the remembered symbol, else preselects the first listing, then loads its candles:

```csharp
    protected override async Task OnInitializedAsync()
    {
        try
        {
            var rows = await Db.InstrumentListings
                .Where(x => x.ProviderSymbol != "")
                .OrderBy(x => x.ProviderSymbol)
                .Select(x => new { x.ProviderSymbol, x.Isin, x.Currency })
                .ToListAsync();

            _listings = rows
                .Select(x => new ListingOption(x.ProviderSymbol, x.Isin, x.Currency))
                .ToList();

            // Restore the remembered selection, else preselect the first listing.
            var remembered = ChartSelection.SelectedPriceSymbol;
            var initial = (remembered is not null
                ? _listings.FirstOrDefault(o => o.ProviderSymbol == remembered)
                : null) ?? _listings.FirstOrDefault();

            if (initial is not null)
            {
                await OnSymbolChanged(initial);
            }
        }
        catch (Exception ex)
        {
            _error = $"Instrumente konnten nicht geladen werden: {ex.Message}";
        }
    }
```

- [x] **Step 3: Persist the selection on change**

In `OnSymbolChanged`, after `_selected = option;`, record it:

```csharp
    private async Task OnSymbolChanged(ListingOption? option)
    {
        _selected = option;
        ChartSelection.SelectedPriceSymbol = option?.ProviderSymbol;
        _candles = new();
        _error = null;
        if (option is not null)
        {
            // ... unchanged body that loads _candles ...
        }
    }
```

(Leave the candle-loading body exactly as it is.)

- [x] **Step 4: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: build succeeds.

- [x] **Step 5: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Browse/PriceChart.razor
git commit -m "Feat: Kurschart preselects, remembers selection, opens zoomed to last year"
```

---

## Task 8: Steuerreport scroll-and-highlight (item 8)

**Files:**
- Modify: `src/WealthIQ.Web/wwwroot/wealthiq.js`
- Modify: `src/WealthIQ.Web/wwwroot/wealthiq.css`
- Modify: `src/WealthIQ.Web/Components/Pages/Steuerreport.razor:251-283`

- [x] **Step 1: Add a scroll-and-highlight helper**

In `wealthiq.js`, inside the `window.wealthiq` object, add (next to the existing `scrollToAnchor`):

```javascript
    scrollAndHighlight: function (id) {
        var el = document.getElementById(id);
        if (!el) return;
        el.scrollIntoView({ behavior: 'smooth', block: 'center' });
        var row = el.closest('tr') || el;
        row.classList.remove('wiq-row-highlight');
        // Force reflow so re-adding the class restarts the animation.
        void row.offsetWidth;
        row.classList.add('wiq-row-highlight');
        window.setTimeout(function () { row.classList.remove('wiq-row-highlight'); }, 2200);
    },
```

> If `scrollToAnchor` already exists and is only used by this link, you may instead extend it; keeping a new named function avoids touching other callers.

- [x] **Step 2: Add the highlight animation CSS**

Append to `wealthiq.css`:

```css
/* Transient highlight when jumping from a Verkäufe row to its detail row. */
@keyframes wiq-row-flash {
    0%   { background-color: rgba(52, 211, 153, 0.35); }
    100% { background-color: transparent; }
}
.wiq-row-highlight > td {
    animation: wiq-row-flash 2.2s ease-out;
}
@media (prefers-reduced-motion: reduce) {
    .wiq-row-highlight > td {
        animation: none;
        background-color: rgba(52, 211, 153, 0.18);
    }
}
```

- [x] **Step 3: Point the link at the new helper**

In `Steuerreport.razor`, change `ScrollToDetail` to call the new helper:

```csharp
    private async Task ScrollToDetail(int index)
        => await JS.InvokeVoidAsync("wealthiq.scrollAndHighlight", $"sell-detail-{index}");
```

The anchor `<span id="sell-detail-{index}">` already lives inside the detail row's first `<td>`, so `closest('tr')` resolves to the correct row.

- [x] **Step 4: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: build succeeds. (Behavior verified in Task 11 smoke test.)

- [x] **Step 5: Commit**

```bash
git add src/WealthIQ.Web/wwwroot/wealthiq.js src/WealthIQ.Web/wwwroot/wealthiq.css src/WealthIQ.Web/Components/Pages/Steuerreport.razor
git commit -m "Feat: Verkäufe 'Anzeigen' scrolls to and highlights the detail row"
```

---

## Task 9: Steuerreport inline source/explanation expanders + column removal (items 9 & 10)

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Steuerreport.razor`

This task replaces the navigating "Quelle/Import/Anzeigen" buttons in the **Verkäufe — Details, Dividenden, Quellensteuer, Vorabpauschale** tables with in-place expanders, and hides the Vorab column where it is always 0. The Verkäufe summary table keeps its row-highlight link from Task 8.

- [x] **Step 1: Make the Vorab column conditional in EntryTable (item 10)**

Change the `EntryTable` signature and the two places the Vorab column is rendered. Replace the `EntryTable` render fragment header so it takes a `showVorab` flag (default false), and gate the Vorab `<MudTh>`/`<MudTd>` on it:

```csharp
    private RenderFragment EntryTable(IReadOnlyList<GermanTaxEntry> entries, bool showIsin = true, bool linkToDetail = false, bool showVorab = false) => __builder =>
    {
        var rowList = entries as List<GermanTaxEntry> ?? entries.ToList();
        if (rowList.Count == 0)
        {
            <MudText Typo="Typo.body2">Keine Einträge.</MudText>
        }
        else
        {
            <MudTable Items="rowList" Dense="true" Hover="true" Breakpoint="Breakpoint.Sm">
                <HeaderContent>
                    <MudTh>Datum</MudTh>
                    <MudTh>Symbol</MudTh>
                    @if (showIsin) { <MudTh>ISIN</MudTh> }
                    <MudTh Style="text-align:right">Brutto (€)</MudTh>
                    <MudTh Style="text-align:right">Steuerpflichtig (€)</MudTh>
                    @if (showVorab) { <MudTh Style="text-align:right">Verrechn. Vorabpausch. (€)</MudTh> }
                    <MudTh>Quelle</MudTh>
                </HeaderContent>
                <RowTemplate Context="row">
                    <MudTd DataLabel="Datum">@row.Date.ToString("yyyy-MM-dd")</MudTd>
                    <MudTd DataLabel="Symbol">@row.Symbol</MudTd>
                    @if (showIsin) { <MudTd DataLabel="ISIN">@row.Isin</MudTd> }
                    <MudTd DataLabel="Brutto" Style="text-align:right">@row.RawAmount.ToString("N2")</MudTd>
                    <MudTd DataLabel="Steuerpflichtig" Style="text-align:right">@row.TaxableAmount.ToString("N2")</MudTd>
                    @if (showVorab) { <MudTd DataLabel="Vorabpauschale" Style="text-align:right">@row.UsedVorabpauschale.ToString("N2")</MudTd> }
                    <MudTd DataLabel="Quelle">
                        @if (linkToDetail)
                        {
                            <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Primary"
                                       OnClick="() => ScrollToDetail(rowList.IndexOf(row))">Anzeigen</MudButton>
                        }
                        else
                        {
                            <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Primary"
                                       OnClick="() => ToggleDetail(row)">@(IsExpanded(row) ? "Weniger" : "Quelle")</MudButton>
                        }
                    </MudTd>
                </RowTemplate>
                <ChildRowContent Context="row">
                    @if (!linkToDetail && IsExpanded(row))
                    {
                        <MudTr>
                            <td colspan="@ColSpan(showIsin, showVorab)">
                                @SourceDetail(row)
                            </td>
                        </MudTr>
                    }
                </ChildRowContent>
            </MudTable>
        }
    };
```

> `ChildRowContent` is a MudTable feature that renders an extra row beneath each item — used here as the in-place expander. `ColSpan` accounts for the optional columns.

- [x] **Step 2: Add expander state, colspan, and the detail fragment**

Add to the `@code` block:

```csharp
    private readonly HashSet<(int, DateOnly, string, GermanTaxEntryType)> _expanded = new();

    private static (int, DateOnly, string, GermanTaxEntryType) Key(GermanTaxEntry e)
        => (e.Year, e.Date, e.Isin, e.Type);

    private bool IsExpanded(GermanTaxEntry e) => _expanded.Contains(Key(e));

    private void ToggleDetail(GermanTaxEntry e)
    {
        var key = Key(e);
        if (!_expanded.Remove(key)) { _expanded.Add(key); }
    }

    // Datum, Symbol, [ISIN], Brutto, Steuerpflichtig, [Vorab], Quelle
    private static int ColSpan(bool showIsin, bool showVorab) => 4 + (showIsin ? 1 : 0) + (showVorab ? 1 : 0);

    private RenderFragment SourceDetail(GermanTaxEntry row) => __builder =>
    {
        <div class="pa-3" style="background:var(--mud-palette-background-grey);">
            @if (row.Type == GermanTaxEntryType.Vorabpauschale)
            {
                <MudText Typo="Typo.body2">
                    Jahresanfangskurs: @row.YearStartPrice.ToString("N2") € &nbsp;·&nbsp;
                    Jahresendkurs: @row.YearEndPrice.ToString("N2") € &nbsp;·&nbsp;
                    Basiszins: @row.BasisRate.ToString("P2")
                </MudText>
                <MudText Typo="Typo.body2">
                    Gehaltene Stück: @row.HeldQuantity.ToString("0.####") &nbsp;·&nbsp;
                    Ausschüttung/Anteil: @row.DistributionPerShare.ToString("N4") € &nbsp;·&nbsp;
                    Monatsfaktor: @row.MonthFactor.ToString("0.##")
                </MudText>
            }
            else
            {
                <MudText Typo="Typo.body2">
                    Quelle: @row.SourceReference &nbsp;·&nbsp; Datei: @row.SourceFile
                </MudText>
                @if (!string.IsNullOrWhiteSpace(row.OriginalCurrency))
                {
                    <MudText Typo="Typo.body2">
                        Original: @row.OriginalAmount.ToString("N2") @row.OriginalCurrency (@row.Date.ToString("yyyy-MM-dd"))
                    </MudText>
                }
            }
        </div>
    };
```

- [x] **Step 3: Update the panel call-sites**

In the markup, set the flags so Vorab shows only for Verkäufe summary and the source-expander is used elsewhere:

```razor
            <MudExpansionPanel Text="@($"Verkäufe (realisierter PnL) ({Current.Sells.Count})")">
                @EntryTable(Current.Sells, showIsin: true, linkToDetail: true, showVorab: true)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Verkäufe — Details ({Current.Sells.Count})")">
                @SellDetailTable(Current.Sells)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Vorabpauschale ({Current.Vorabpauschale.Count})")">
                @EntryTable(Current.Vorabpauschale, showIsin: true, showVorab: false)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Dividenden ({Current.Dividends.Count})")">
                @EntryTable(Current.Dividends, showIsin: true, showVorab: false)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Zinsen ({Current.Interest.Count})")">
                @EntryTable(Current.Interest, showIsin: false, showVorab: false)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Quellensteuer ({Current.WithholdingTaxes.Count})")">
                @WithholdingTable(Current.WithholdingTaxes)
            </MudExpansionPanel>
```

- [x] **Step 4: Convert the Verkäufe — Details "Import" link and Quellensteuer link to expanders**

In `SellDetailTable`, replace the "Import" button cell with an expander toggle and add a `ChildRowContent` (the detail row has 11 columns):

```razor
                    <MudTd DataLabel="Quelle">
                        <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Primary"
                                   OnClick="() => ToggleDetail(row)">@(IsExpanded(row) ? "Weniger" : "Quelle")</MudButton>
                    </MudTd>
```

Add a `ChildRowContent` to the `SellDetailTable` MudTable (sells show open + close references):

```razor
                <ChildRowContent Context="row">
                    @if (IsExpanded(row))
                    {
                        <MudTr>
                            <td colspan="11">
                                <div class="pa-3" style="background:var(--mud-palette-background-grey);">
                                    <MudText Typo="Typo.body2">
                                        Kauf-Referenz: @row.SourceReference &nbsp;·&nbsp;
                                        Verkauf-Referenz: @row.CloseReference &nbsp;·&nbsp;
                                        Datei: @row.SourceFile
                                    </MudText>
                                </div>
                            </td>
                        </MudTr>
                    }
                </ChildRowContent>
```

In `WithholdingTable`, replace the "Anzeigen" button cell with the expander toggle and add a `ChildRowContent` (5 columns):

```razor
                    <MudTd DataLabel="Quelle">
                        <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Primary"
                                   OnClick="() => ToggleDetail(row)">@(IsExpanded(row) ? "Weniger" : "Quelle")</MudButton>
                    </MudTd>
```

```razor
                <ChildRowContent Context="row">
                    @if (IsExpanded(row))
                    {
                        <MudTr>
                            <td colspan="5">@SourceDetail(row)</td>
                        </MudTr>
                    }
                </ChildRowContent>
```

- [x] **Step 5: Remove the now-unused navigation**

Delete the `DrillToSource` method and the `@inject NavigationManager Navigation` if no longer referenced anywhere in `Steuerreport.razor`. (Search the file for `DrillToSource` and `Navigation.` first; remove both only if there are no remaining uses.)

- [x] **Step 6: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: build succeeds.

- [x] **Step 7: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Steuerreport.razor
git commit -m "Feat: Steuerreport shows source/Vorab details inline; drop always-zero Vorab column"
```

---

## Task 10: Ledger screen — account selector + delete (items 1 & 2)

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Browse/LedgerBrowser.razor`

- [x] **Step 1: Inject clear service + dialog and add an account select**

At the top of `LedgerBrowser.razor`, add the injections and usings:

```razor
@using WealthIQ.Application.Persistence.Interface
@inject ILedgerStore LedgerStore
@inject ILedgerClearService LedgerClear
@inject IDialogService DialogService
```

(Remove `@inject NavigationManager Navigation` — no longer used after Step 4.)

Add an account select to the `PageHeader`:

```razor
<PageHeader Title="Ledger" Subtitle="Importierte Buchungen (Originalwährung)">
    <Actions>
        @if (_accounts.Count > 0)
        {
            <MudSelect T="string" Value="_selectedAccountId" ValueChanged="OnAccountChanged" Label="Konto"
                       Variant="Variant.Outlined" Dense="true" Style="min-width:220px;">
                @foreach (var acc in _accounts)
                {
                    <MudSelectItem T="string" Value="@acc.Id">@acc.Number</MudSelectItem>
                }
            </MudSelect>
        }
    </Actions>
</PageHeader>
```

- [x] **Step 2: Load accounts, filter entries by selected account**

Rework the `@code` block so the raw ledger is held once and the per-kind lists are rebuilt for the selected account. Replace the data-loading section:

```csharp
    private bool _loading = true;
    private string? _error;

    private sealed record AccountOption(string Id, string Number);
    private sealed record TradeView(DateOnly Date, TradeSide Side, string Symbol, string Isin,
        decimal Quantity, decimal UnitPrice, decimal Fees, decimal Taxes, Currency Currency, Guid AccountId);
    private sealed record CashView(DateOnly Date, string Symbol, string Isin, CashFlowType Type,
        decimal GrossAmount, decimal Fees, decimal Taxes, Currency Currency, Guid AccountId);

    private List<AccountOption> _accounts = new();
    private string _selectedAccountId = "";

    private List<TradeView> _allTrades = new();
    private List<CashView> _allCash = new();

    private List<TradeView> _trades = new();
    private List<CashView> _dividends = new();
    private List<CashView> _interest = new();
    private List<CashView> _withholding = new();
    private List<CashView> _other = new();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            _loading = true;
            _allTrades = new();
            _allCash = new();

            var ledger = await LedgerStore.LoadLedgerAsync();
            var byId = ledger.Instruments.ToDictionary(i => i.InstrumentId);
            var accountNumberById = ledger.Accounts.ToDictionary(a => a.AccountId.Value, a => a.AccountNumber);

            foreach (var entry in ledger.Entries)
            {
                switch (entry)
                {
                    case TradeEntry t:
                        var (ts, ti) = Resolve(t.InstrumentId, byId);
                        _allTrades.Add(new TradeView(t.EffectiveDate, t.Side, ts, ti,
                            t.Quantity.Value, t.UnitPrice.Amount, t.Fees.Amount, t.Taxes.Amount, t.UnitPrice.Currency, t.AccountId.Value));
                        break;
                    case CashEntry c:
                        var srcId = c.RelatedInstrumentId ?? c.CashInstrumentId;
                        var (cs, ci) = Resolve(srcId, byId);
                        _allCash.Add(new CashView(c.EffectiveDate, cs, ci, c.CashFlowType,
                            c.GrossAmount.Amount, c.Fees.Amount, c.Taxes.Amount, c.GrossAmount.Currency, c.AccountId.Value));
                        break;
                }
            }

            // Accounts that actually have entries.
            var accountIdsWithEntries = _allTrades.Select(x => x.AccountId)
                .Concat(_allCash.Select(x => x.AccountId))
                .Distinct()
                .ToList();

            _accounts = accountIdsWithEntries
                .Select(id => new AccountOption(
                    id.ToString(),
                    accountNumberById.TryGetValue(id, out var num) ? num : id.ToString()))
                .OrderBy(a => a.Number)
                .ToList();

            _selectedAccountId = _accounts.FirstOrDefault()?.Id ?? "";
            ApplyAccountFilter();
        }
        catch (Exception ex)
        {
            _error = $"Ledger konnte nicht geladen werden: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnAccountChanged(string accountId)
    {
        _selectedAccountId = accountId;
        ApplyAccountFilter();
    }

    private void ApplyAccountFilter()
    {
        var match = Guid.TryParse(_selectedAccountId, out var id) ? id : Guid.Empty;
        _trades = _allTrades.Where(x => x.AccountId == match).ToList();
        var cash = _allCash.Where(x => x.AccountId == match).ToList();
        _dividends = cash.Where(x => x.Type == CashFlowType.Dividend).ToList();
        _interest = cash.Where(x => x.Type == CashFlowType.Interest).ToList();
        _withholding = cash.Where(x => x.Type == CashFlowType.WithholdingTax).ToList();
        _other = cash.Where(x => x.Type is not (CashFlowType.Dividend or CashFlowType.Interest or CashFlowType.WithholdingTax)).ToList();
    }

    private static (string Symbol, string Isin) Resolve(InstrumentId id, IReadOnlyDictionary<InstrumentId, Instrument> byId)
        => byId.TryGetValue(id, out var i) ? (i.Symbol, i.ISIN) : ("", "");
```

> The `CashTable` render fragment is unchanged — it already takes an `IReadOnlyList<CashView>`. The `TradeView`/`CashView` records gained an `AccountId` field; the table markup ignores it.

- [x] **Step 3: Add the delete controls (item 2) and remove the old SourceLink**

Remove the `SourceLink`/`DrillToSource` members and the empty trailing `<MudTh></MudTh>` / `<MudTd>@SourceLink(...)</MudTd>` cells from both the trades table and `CashTable` (the source link moves out of the ledger browser; the Audit page remains for provenance).

Add a delete section below the expansion panels (inside the `else` block, after `</MudExpansionPanels>` and before its closing `</div>`):

```razor
        <div class="mt-6">
            <SectionCard Title="Ledger verwalten">
                <ChildContent>
                    <MudCheckBox @bind-Value="_purgeAuditFiles" Label="Rohdateien (Audit) löschen" />
                    <div class="mt-2">
                        <MudButton Variant="Variant.Filled" Color="Color.Error"
                                   Disabled="_busy" OnClick="ClearLedger">
                            Ledger leeren (unwiderruflich)
                        </MudButton>
                    </div>
                </ChildContent>
            </SectionCard>
        </div>
```

Add the supporting state + handler to `@code`:

```csharp
    private bool _busy;
    private bool _purgeAuditFiles;

    private async Task ClearLedger()
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Ledger wirklich löschen?",
            "Alle Buchungen, Konten, Import-Batches und Diagnosen werden unwiderruflich gelöscht. Referenz-/Marktdaten bleiben erhalten.",
            yesText: "Endgültig löschen", cancelText: "Abbrechen");
        if (confirmed != true) return;

        _busy = true;
        try
        {
            await LedgerClear.ClearLedgerAsync(_purgeAuditFiles);
            await LoadAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _busy = false; }
    }
```

- [x] **Step 4: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: build succeeds.

- [x] **Step 5: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Browse/LedgerBrowser.razor
git commit -m "Feat: Ledger screen scoped by account with in-place delete"
```

---

## Task 11: Marktdaten cleanup — remove panels, FX UI, Basiszins streamline (items 2, 3, 6, 7)

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/DataAdmin.razor`

- [x] **Step 1: Remove the Ledger and Instrumente panels (items 2 & 7)**

Delete the entire `<!-- Ledger -->` `MudExpansionPanel` (Text="Ledger (Buchungen)") and the entire `<!-- Instruments -->` `MudExpansionPanel` (Text="Instrumente"). Then remove the now-unused members and injections:
- `@inject ILedgerClearService LedgerClear`
- fields `_ledgerEntries, _accounts, _batches`, `_profileCount, _listingCount`, `_purgeAuditFiles`
- the `ClearLedger` method
- in `LoadStatusAsync`, delete the lines computing `_ledgerEntries/_accounts/_batches/_profileCount/_listingCount`.

- [x] **Step 2: Add FX incremental + add-currency UI (item 3)**

Replace the FX panel body (`<!-- FX Rates -->` panel) with:

```razor
    <!-- FX Rates -->
    <MudExpansionPanel Text="Wechselkurse (EZB)" Icon="@Icons.Material.Outlined.CurrencyExchange">
        <MudText Typo="Typo.body2" Class="mb-3">
            @if (_fxMin is not null && _fxMax is not null)
            {
                <span>Zeitraum: @_fxMin?.ToString("yyyy-MM-dd") – @_fxMax?.ToString("yyyy-MM-dd") | @_fxCurrencies Währungen</span>
            }
            else
            {
                <span>keine Daten</span>
            }
            <span> | Zuletzt aktualisiert: @(_fxLastRefreshed?.ToString("yyyy-MM-dd HH:mm") ?? "—")</span>
        </MudText>

        <MudButton Variant="Variant.Outlined" Color="Color.Primary" Class="mr-2 mb-2"
                   Disabled="_busy" OnClick="RefreshFxIncremental">
            Inkrementell aktualisieren
        </MudButton>

        <MudDivider Class="my-3" />
        <MudText Typo="Typo.subtitle2" Class="mb-2">Währung hinzufügen (Backfill)</MudText>
        <div class="d-flex gap-3 align-center flex-wrap mb-3">
            <MudSelect T="string" @bind-Value="_newCurrency" Label="Währung" Variant="Variant.Outlined" Style="max-width: 200px;">
                @foreach (var ccy in EcbCurrencies)
                {
                    <MudSelectItem T="string" Value="@ccy">@ccy</MudSelectItem>
                }
            </MudSelect>
            <MudButton Variant="Variant.Outlined" Color="Color.Primary"
                       Disabled="_busy || string.IsNullOrWhiteSpace(_newCurrency)" OnClick="AddFxCurrency">
                Hinzufügen + Backfill
            </MudButton>
        </div>

        <MudDivider Class="my-3" />
        <MudText Typo="Typo.subtitle2" Class="mb-2">Gezielter Zeitraum</MudText>
        <MudDateRangePicker @bind-DateRange="_fxRange" Label="Zeitraum" Variant="Variant.Outlined" Class="mb-3" />
        <MudButton Variant="Variant.Outlined" Color="Color.Primary" Class="mr-2 mb-2"
                   Disabled="_busy" OnClick="RefreshFxRates">
            Von EZB aktualisieren
        </MudButton>
        <MudButton Variant="Variant.Outlined" Color="Color.Warning" Class="mr-2 mb-2"
                   Disabled="_busy" OnClick="ClearFxRates">
            Löschen
        </MudButton>
        <MudButton Variant="Variant.Outlined" Class="mb-2"
                   Disabled="_busy" OnClick="ReseedFxRates">
            Aus Datei neu laden
        </MudButton>
    </MudExpansionPanel>
```

Add the supporting members to `@code`:

```csharp
    private string _newCurrency = "";

    // ECB eurofxref-hist publishes this set (EUR is the base). Static = deterministic dropdown.
    private static readonly string[] EcbCurrencies =
    [
        "USD", "JPY", "BGN", "CZK", "DKK", "GBP", "HUF", "PLN", "RON", "SEK",
        "CHF", "ISK", "NOK", "TRY", "AUD", "BRL", "CAD", "CNY", "HKD", "IDR",
        "ILS", "INR", "KRW", "MXN", "MYR", "NZD", "PHP", "SGD", "THB", "ZAR"
    ];

    private async Task RefreshFxIncremental()
    {
        _busy = true;
        _lastResult = null;
        try
        {
            var result = await FxRefresh.RefreshIncrementalAsync(Today(), default);
            _lastResult = result;
            _lastResultLabel = "Wechselkurse (inkrementell)";
            await RefreshLog.RecordAsync("FxRates", Clock.GetUtcNow(),
                $"{result.Added} neu, {result.Updated} aktualisiert");
            ShowSuccess($"Wechselkurse aktualisiert: {result.Added} neu, {result.Updated} aktualisiert.");
            await LoadStatusAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private async Task AddFxCurrency()
    {
        _busy = true;
        _lastResult = null;
        try
        {
            var result = await FxRefresh.AddCurrencyAsync(_newCurrency, Today().AddYears(-25), Today(), default);
            _lastResult = result;
            _lastResultLabel = $"Wechselkurse {_newCurrency} (Backfill)";
            await RefreshLog.RecordAsync("FxRates", Clock.GetUtcNow(),
                $"{_newCurrency} Backfill: {result.Added} neu");
            ShowSuccess($"{_newCurrency} hinzugefügt: {result.Added} Kurse geladen.");
            _newCurrency = "";
            await LoadStatusAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }
```

(Keep the existing `RefreshFxRates`, `ClearFxRates`, `ReseedFxRates`, and `_fxRange`.)

- [x] **Step 3: Streamline the Basiszins panel (item 6)**

Replace the Basiszins panel body so it is a single editable table with an inline add-row. Replace the manual-entry section, the BMF section, and the "Gespeicherte Werte" block with:

```razor
    <!-- Basiszins -->
    <MudExpansionPanel Text="Basiszins (BMF)" Icon="@Icons.Material.Outlined.Percent">
        <MudText Typo="Typo.body2" Class="mb-3">
            @if (_basiszinsMin is not null && _basiszinsMax is not null)
            {
                <span>Jahre: @_basiszinsMin – @_basiszinsMax | @_basiszinsCount Einträge</span>
            }
            else
            {
                <span>keine Daten</span>
            }
        </MudText>

        @if (_basiszinsRows.Count > 0)
        {
            <MudTable Items="_basiszinsRows" Dense="true" Hover="true" Elevation="0"
                      CanCancelEdit="true" RowEditCommit="@(async (obj) => await CommitBasiszinsEdit(obj))" T="BasiszinsRow"
                      Class="mb-2" Style="max-width:480px;">
                <HeaderContent>
                    <MudTh>Jahr</MudTh>
                    <MudTh Style="text-align:right">Zinssatz</MudTh>
                    <MudTh></MudTh>
                </HeaderContent>
                <RowTemplate>
                    <MudTd DataLabel="Jahr">@context.Year</MudTd>
                    <MudTd DataLabel="Zinssatz" Style="text-align:right">@context.Rate.ToString("F4")</MudTd>
                    <MudTd Style="text-align:right">
                        <MudIconButton Icon="@Icons.Material.Outlined.Delete" Size="Size.Small" Color="Color.Error"
                                       Disabled="_busy" OnClick="() => DeleteBasiszins(context.Year)" aria-label="Löschen" />
                    </MudTd>
                </RowTemplate>
                <RowEditingTemplate>
                    <MudTd DataLabel="Jahr">@context.Year</MudTd>
                    <MudTd DataLabel="Zinssatz">
                        <MudNumericField @bind-Value="context.Rate" Format="F4" Variant="Variant.Text" Style="max-width:160px;" />
                    </MudTd>
                    <MudTd></MudTd>
                </RowEditingTemplate>
            </MudTable>
            <MudText Typo="Typo.caption" Style="color:var(--mud-palette-text-secondary);">
                Zeile anklicken zum Bearbeiten des Zinssatzes.
            </MudText>
        }
        else
        {
            <MudText Typo="Typo.body2" Class="mb-2">Keine Einträge.</MudText>
        }

        <MudDivider Class="my-3" />
        <MudText Typo="Typo.subtitle2" Class="mb-2">Jahr hinzufügen</MudText>
        <div class="d-flex gap-3 align-center flex-wrap mb-3">
            <MudNumericField T="int" @bind-Value="_manualYear" Label="Jahr" Variant="Variant.Outlined" Style="max-width: 160px;" />
            <MudNumericField T="decimal" @bind-Value="_manualRate" Label="Zinssatz (z. B. 0,0253)" Variant="Variant.Outlined" Style="max-width: 220px;" Format="F4" />
            <MudButton Variant="Variant.Outlined" Color="Color.Primary"
                       Disabled="_busy" OnClick="SaveManualBasiszins">
                Speichern
            </MudButton>
        </div>

        <MudDivider Class="my-3" />
        <MudButton Variant="Variant.Outlined" Color="Color.Warning" Class="mr-2 mb-2"
                   Disabled="_busy" OnClick="ClearBasiszins">
            Löschen
        </MudButton>
        <MudButton Variant="Variant.Outlined" Class="mb-2"
                   Disabled="_busy" OnClick="ReseedBasiszins">
            Aus Datei neu laden
        </MudButton>
    </MudExpansionPanel>
```

Then in `@code`:
- Change the rows query in `LoadStatusAsync` to sort **ascending**: `.OrderBy(x => x.Year)` (was `OrderByDescending`).
- Remove `_basisYear`, the `RefreshBasiszins` method, and the `_basisYear` initialization in `OnInitializedAsync`.
- Remove the `_basiszinsLastRefreshed` usage from the Basiszins panel header (already removed in the markup above); you may keep the field/log line or delete it — delete `_basiszinsLastRefreshed` and its `RefreshLog.GetLastRefreshedAsync("Basiszins")` line for cleanliness.

Keep `SaveManualBasiszins`, `CommitBasiszinsEdit`, `DeleteBasiszins`, `_manualYear`, `_manualRate` (still used by add-row + edit).

- [x] **Step 4: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: build succeeds (no references to removed members remain).

- [x] **Step 5: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/DataAdmin.razor
git commit -m "Refine: Marktdaten — drop Ledger/Instrumente panels, FX incremental/add-currency, simpler Basiszins"
```

---

## Task 12: Full verification, docs, and manual smoke test

**Files:**
- Modify: `CLAUDE.md` (Web UI section) — reflect the moved ledger delete, FX incremental/add-currency, streamlined Basiszins, removed Marktdaten panels, and Kurschart/Steuerreport changes.

- [x] **Step 1: Format**

Run: `dotnet format WealthIQ.slnx`
Expected: no errors. Re-run `dotnet format WealthIQ.slnx --verify-no-changes` → clean.

- [x] **Step 2: Full test suite (Release, as CI runs)**

Run: `dotnet test WealthIQ.slnx --configuration Release`
Expected: all tests pass, including `GermanTaxRegressionTests` with unchanged expected figures.

- [x] **Step 3: Manual smoke test (`dotnet run`)**

Per project memory, Blazor render correctness is not covered by build/xUnit — run the app and verify:

```bash
dotnet run --project src/WealthIQ.Web
```

Checklist (with seeded/imported data present):
- **Ledger** (`/browse/ledger`): account dropdown lists accounts; switching filters all tables; "Ledger leeren" prompts + works; the per-row "Anzeigen" source link is gone.
- **Marktdaten** (`/data-admin`): no Ledger panel, no Instrumente panel; FX panel has "Inkrementell aktualisieren" + "Währung hinzufügen" (try adding e.g. JPY) ; Basiszins is one editable table sorted ascending with inline add + delete, no BMF button.
- **Kurschart** (`/browse/prices`): first instrument auto-selected; chart opens zoomed to ~last year; no "x" clear; pick another instrument, navigate away and back → selection remembered.
- **Steuerreport** (`/`): "Verkäufe → Anzeigen" scrolls to + flashes the matching detail row; "Quelle" toggles an inline detail under Verkäufe-Details/Dividenden/Quellensteuer; Vorabpauschale "Quelle" shows the calculation inputs; Dividenden & Zinsen no longer show the "Verrechnete Vorabpauschale" column.

- [x] **Step 4: Update CLAUDE.md**

Edit the "Web UI" section to reflect: ledger delete now lives on the Ledger browser (account-scoped); Marktdaten no longer hosts Ledger or Instrumente; FX supports incremental refresh + add-currency backfill (tracked set = stored currencies ∪ USD/GBP/CHF); Basiszins is a single ascending editable table (no BMF button in UI); Kurschart preselects/remembers/opens zoomed to last year; Steuerreport uses inline source/Vorab expanders and drops the always-zero Vorab column from Dividenden/Zinsen.

- [x] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "Docs: update CLAUDE.md for Phase 3 streamlining"
```

---

## Self-review notes (coverage map)

- Item 1 (ledger grouped/selectable by account) → Task 10.
- Item 2 (move ledger delete; clean Marktdaten) → Tasks 10 + 11.
- Item 3 (FX incremental + add-currency backfill) → Tasks 1, 2, 3 (backend) + 11 (UI).
- Item 4 (Kurschart preselect/remember/no-x) → Tasks 5 + 7.
- Item 5 (chart last-year zoom) → Tasks 6 + 7.
- Item 6 (Basiszins single editable table, sort by year) → Task 11.
- Item 7 (remove Instrumente from Marktdaten) → Task 11.
- Item 8 (Verkäufe → detail row highlight) → Task 8.
- Item 9 (inline source/explanation expanders) → Tasks 4 (backend) + 9 (UI).
- Item 10 (remove Vorab column from Dividenden/Zinsen) → Task 9.

**Type consistency:** `FetchAsync(from, to, currencies, ct)` used identically in Tasks 1 & 3; `IFxRateStore.GetStoredCurrencies()`/`GetMaxStoredDate()` defined in Task 2 and consumed in Task 3; `RefreshIncrementalAsync`/`AddCurrencyAsync` defined in Task 3 and called in Task 11; `ChartSelectionState.SelectedPriceSymbol` defined in Task 5 and used in Task 7; `InitialRangeDays` defined in Task 6 and used in Task 7; `GermanTaxEntry` new fields defined in Task 4 and rendered in Task 9; `EntryTable(..., showVorab)` and `ToggleDetail/IsExpanded/SourceDetail/ColSpan` all defined and used within Task 9.
