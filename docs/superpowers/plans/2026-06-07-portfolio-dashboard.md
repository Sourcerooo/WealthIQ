# Portfolio Dashboard ("Mein Portfolio") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a polished "Mein Portfolio" dashboard at `/` showing current holdings (grouped by ISIN), their EUR worth, unrealized gain since purchase, asset-class allocation, YTD KPIs, a manual price-refresh, and a master–detail price chart.

**Architecture:** Extend the existing (dormant, tested) `PortfolioValuationService` to be the single source of valuation truth (cost basis + average buy price + unrealized P&L, resilient to missing prices). Add a thin `PortfolioDashboardService` (Application) for ISIN aggregation, the "Alle" rollup, and YTD KPIs. A new Blazor page (`Web/Components/Pages/Dashboard/`) composes and renders, reusing `LightweightChart`, `StatCard`, MudBlazor `MudChart` donut, and `HistoricalPriceRefreshService`.

**Tech Stack:** C# / .NET 10, Blazor Server + MudBlazor, EF Core + SQLite, TradingView Lightweight Charts v4, xUnit.

**Spec:** `docs/superpowers/specs/2026-06-07-portfolio-dashboard-design.md`

---

## File Structure

**Application (logic, unit-tested):**
- Modify `src/WealthIQ.Application/Valuation/PortfolioPositionSnapshot.cs` — add cost basis, avg buy price (native + EUR), unrealized P&L, asset class, provider symbol, effective price date, price-missing flag.
- Modify `src/WealthIQ.Application/Valuation/PortfolioValuationService.cs` — compute the new fields; make per-position pricing resilient (flag instead of throw).
- Create `src/WealthIQ.Application/Dashboard/DashboardContracts.cs` — `DashboardHolding`, `DashboardAllocationSlice`, `DashboardKpis`, `DashboardView`, `PortfolioDashboardReport`.
- Create `src/WealthIQ.Application/Dashboard/PortfolioDashboardService.cs` — per-account + "Alle" rollup, allocation, YTD dividends/realized.

**Web (composition + UI, manual smoke test):**
- Modify `src/WealthIQ.Web/Program.cs` — register `PortfolioValuationService` and `PortfolioDashboardService`.
- Modify `src/WealthIQ.Web/Components/Shared/StatCard.razor` — optional `ValueText` override (for non-€ values like counts/percent).
- Modify `src/WealthIQ.Web/Components/Shared/LightweightChart.razor` + `src/WealthIQ.Web/wwwroot/wiq-charts.js` — optional dashed reference price line.
- Modify `src/WealthIQ.Web/Components/Pages/Steuerreport.razor` — re-route from `/` to `/steuerreport`.
- Modify `src/WealthIQ.Web/Components/Layout/MainLayout.razor` — new "Portfolio" nav group; point Steuerreport at `/steuerreport`.
- Create `src/WealthIQ.Web/Components/Pages/Dashboard/Dashboard.razor` — the new landing page.

**Tests:**
- Modify `tests/WealthIQ.Tests/Application/Valuation/PortfolioValuationServiceTests.cs`.
- Create `tests/WealthIQ.Tests/Application/Dashboard/PortfolioDashboardServiceTests.cs`.

**Docs:**
- Modify `CLAUDE.md`.

---

## Task 1: Extend the valuation snapshot & service

**Files:**
- Modify: `src/WealthIQ.Application/Valuation/PortfolioPositionSnapshot.cs`
- Modify: `src/WealthIQ.Application/Valuation/PortfolioValuationService.cs`
- Test: `tests/WealthIQ.Tests/Application/Valuation/PortfolioValuationServiceTests.cs`

- [ ] **Step 1: Extend the snapshot record**

Replace the entire body of `PortfolioPositionSnapshot.cs` with:

```csharp
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.Valuation;

public sealed record PortfolioPositionSnapshot(
    AccountId AccountId,
    InstrumentId InstrumentId,
    string Symbol,
    string? Isin,
    PositionDirection Direction,
    decimal Quantity,
    decimal ClosePrice,
    CurrencyCode PriceCurrency,
    decimal MarketValueInBaseCurrency,
    decimal CostBasisInBaseCurrency,
    decimal AverageBuyPriceInBaseCurrency,
    decimal? AverageBuyPriceNative,
    CurrencyCode NativeCurrency,
    decimal UnrealizedPnlInBaseCurrency,
    decimal UnrealizedPnlPct,
    string AssetClass,
    string? ProviderSymbol,
    DateOnly EffectivePriceDate,
    bool PriceMissing);
```

- [ ] **Step 2: Write the failing tests**

Replace the two existing `[Fact]` methods in `PortfolioValuationServiceTests.cs` (keep the helper `CreateSourceProvenance` and the four stub classes at the bottom unchanged) with the following three tests. Note: `Calculate_MissingMarketDataMapping` now asserts the resilient *flag* behaviour instead of a throw.

```csharp
    [Fact]
    public void Calculate_LongPositionAndCash_UsesLatestCloseOnOrBeforeDate()
    {
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "IE00TEST1234", "TEST", "Test ETF", 0.30m) { Type = "ETF_EQUITY" };

        var service = new PortfolioValuationService(
            new StubHistoricalPriceLookup(new PriceBar(new DateOnly(2025, 5, 2), "TEST.AS", CurrencyCode.EUR, 100m, 102m, 99m, 101m, 101m, 1000)),
            new StubInstrumentMarketDataMap(new InstrumentMarketDataProfile("YahooFinance", "TEST.AS")),
            new StubFxRateLookup());

        var ledger = new PortfolioLedger([
            new TradeEntry(
                PortfolioEntryId.NewId(), accountId,
                new DateTimeOffset(2025, 5, 1, 10, 0, 0, TimeSpan.Zero), new DateOnly(2025, 5, 1),
                CreateSourceProvenance("BUY-1"), instrumentId, TradeSide.Buy,
                new Quantity(2m), new Money(90m, CurrencyCode.EUR), new Money(1m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR)),
            new CashEntry(
                PortfolioEntryId.NewId(), accountId,
                new DateTimeOffset(2025, 5, 1, 20, 0, 0, TimeSpan.Zero), new DateOnly(2025, 5, 1),
                CreateSourceProvenance("INT-1"), InstrumentId.NewId(), CashFlowType.Interest,
                new Money(10m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR))
        ], [instrument]);

        var snapshot = service.Calculate(ledger, [instrument], new DateOnly(2025, 5, 4));

        Assert.Equal(new DateOnly(2025, 5, 2), snapshot.EffectiveMarketDate);
        Assert.Single(snapshot.Positions);
        var position = snapshot.Positions[0];
        Assert.False(position.PriceMissing);
        Assert.Equal(202m, position.MarketValueInBaseCurrency);
        // Cost basis = 2 * 90 + 1 fee = 181 EUR; avg buy EUR = 90.5; unrealized = 202 - 181 = 21.
        Assert.Equal(181m, position.CostBasisInBaseCurrency);
        Assert.Equal(90.5m, position.AverageBuyPriceInBaseCurrency);
        Assert.Equal(90.5m, position.AverageBuyPriceNative);
        Assert.Equal(21m, position.UnrealizedPnlInBaseCurrency);
        Assert.Equal("ETF_EQUITY", position.AssetClass);
        Assert.Equal("TEST.AS", position.ProviderSymbol);
        Assert.Equal(31m, snapshot.TotalValueInBaseCurrency);
    }

    [Fact]
    public void Calculate_MissingMarketDataMapping_FlagsPositionInsteadOfThrowing()
    {
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "IE00TEST9999", "TEST", "Test ETF", 0.30m) { Type = "ETF_EQUITY" };

        var service = new PortfolioValuationService(
            new StubHistoricalPriceLookup(new PriceBar(new DateOnly(2025, 5, 2), "TEST.AS", CurrencyCode.EUR, 100m, 102m, 99m, 101m, 101m, 1000)),
            new MissingInstrumentMarketDataMap(),
            new StubFxRateLookup());

        var ledger = new PortfolioLedger([
            new TradeEntry(
                PortfolioEntryId.NewId(), accountId,
                new DateTimeOffset(2025, 5, 1, 10, 0, 0, TimeSpan.Zero), new DateOnly(2025, 5, 1),
                CreateSourceProvenance("BUY-1"), instrumentId, TradeSide.Buy,
                new Quantity(1m), new Money(90m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR))
        ], [instrument]);

        var snapshot = service.Calculate(ledger, [instrument], new DateOnly(2025, 5, 4));

        Assert.Single(snapshot.Positions);
        var position = snapshot.Positions[0];
        Assert.True(position.PriceMissing);
        Assert.Equal(0m, position.MarketValueInBaseCurrency);
        // Cost basis is still known from the open lot even though price is unavailable.
        Assert.Equal(90m, position.CostBasisInBaseCurrency);
        // A flagged position contributes 0 to the total (excluded).
        Assert.Equal(0m, snapshot.TotalValueInBaseCurrency);
    }

    [Fact]
    public void Calculate_TwoBuysSamePrice_BlendsAverageBuyPrice()
    {
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "IE00TEST1234", "TEST", "Test ETF", 0.30m) { Type = "ETF_EQUITY" };

        var service = new PortfolioValuationService(
            new StubHistoricalPriceLookup(new PriceBar(new DateOnly(2025, 5, 2), "TEST.AS", CurrencyCode.EUR, 100m, 102m, 99m, 120m, 120m, 1000)),
            new StubInstrumentMarketDataMap(new InstrumentMarketDataProfile("YahooFinance", "TEST.AS")),
            new StubFxRateLookup());

        var ledger = new PortfolioLedger([
            new TradeEntry(PortfolioEntryId.NewId(), accountId,
                new DateTimeOffset(2025, 5, 1, 10, 0, 0, TimeSpan.Zero), new DateOnly(2025, 5, 1),
                CreateSourceProvenance("BUY-1"), instrumentId, TradeSide.Buy,
                new Quantity(2m), new Money(100m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR)),
            new TradeEntry(PortfolioEntryId.NewId(), accountId,
                new DateTimeOffset(2025, 5, 1, 11, 0, 0, TimeSpan.Zero), new DateOnly(2025, 5, 1),
                CreateSourceProvenance("BUY-2"), instrumentId, TradeSide.Buy,
                new Quantity(2m), new Money(110m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR))
        ], [instrument]);

        var snapshot = service.Calculate(ledger, [instrument], new DateOnly(2025, 5, 4));

        var position = snapshot.Positions[0];
        Assert.Equal(4m, position.Quantity);
        // Cost basis 2*100 + 2*110 = 420; avg buy = 105.
        Assert.Equal(420m, position.CostBasisInBaseCurrency);
        Assert.Equal(105m, position.AverageBuyPriceInBaseCurrency);
        Assert.Equal(480m, position.MarketValueInBaseCurrency); // 4 * 120
        Assert.Equal(60m, position.UnrealizedPnlInBaseCurrency);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~PortfolioValuationServiceTests"`
Expected: FAIL — compile errors (snapshot now needs the new constructor args) / assertion failures (new fields not populated).

- [ ] **Step 4: Rewrite `PortfolioValuationService.Calculate` to populate the new fields and be resilient**

Replace the body of the `foreach (var instrumentLots in openLots...)` loop and the cash block in `PortfolioValuationService.cs`. Replace lines 30–73 (from `var positionSnapshots = ...` through the end of the `cashSnapshots` assignment) with:

```csharp
        var positionSnapshots = new List<PortfolioPositionSnapshot>();
        var effectiveMarketDates = new List<DateOnly>();

        foreach (var instrumentLots in openLots
                     .Where(x => x.RemainingQuantity.Value > 0m)
                     .GroupBy(x => new { x.AccountId, x.InstrumentId, x.Direction }))
        {
            var instrument = instrumentById[instrumentLots.Key.InstrumentId];
            var lots = instrumentLots.ToList();
            var quantity = lots.Sum(x => x.RemainingQuantity.Value);

            // Cost basis in EUR: convert each lot's remaining cost at that lot's own open date
            // (the project's FX-at-event-time rule), so mixed-currency lots never average raw prices.
            var costBasisEur = 0m;
            foreach (var lot in lots)
            {
                var lotCostNative = new Money(
                    lot.OpenUnitPrice.Amount * lot.RemainingQuantity.Value + lot.RemainingOpenFees.Amount,
                    lot.OpenUnitPrice.Currency);
                costBasisEur += _fxConverter.Convert(lotCostNative, lot.OpenTradeDate).Amount;
            }

            var nativeCurrency = lots[0].OpenUnitPrice.Currency;
            var singleCurrency = lots.All(x => x.OpenUnitPrice.Currency == nativeCurrency);
            decimal? avgBuyNative = singleCurrency && quantity != 0m
                ? lots.Sum(x => x.OpenUnitPrice.Amount * x.RemainingQuantity.Value + x.RemainingOpenFees.Amount) / quantity
                : null;
            var avgBuyEur = quantity != 0m ? costBasisEur / quantity : 0m;

            var directionSign = instrumentLots.Key.Direction == PositionDirection.Long ? 1m : -1m;

            // Resilient pricing: a missing mapping/price/FX rate must not blank the dashboard.
            decimal closePrice = 0m;
            CurrencyCode priceCurrency = nativeCurrency;
            decimal marketValueEur = 0m;
            DateOnly effectivePriceDate = valuationDate;
            string? providerSymbol = null;
            bool priceMissing = false;
            try
            {
                var marketDataProfile = instrumentMarketDataMap.GetProfile(instrument.ISIN ?? "", nativeCurrency);
                providerSymbol = marketDataProfile.ProviderSymbol;
                var priceBar = historicalPriceLookup.GetPriceBar(
                    valuationDate, marketDataProfile.ProviderSymbol, PriceLookupDateHandling.LatestOnOrBefore);
                closePrice = priceBar.Close;
                priceCurrency = priceBar.Currency;
                effectivePriceDate = priceBar.Date;
                var grossMarketValue = quantity * priceBar.Close * directionSign;
                marketValueEur = _fxConverter.Convert(new Money(grossMarketValue, priceBar.Currency), priceBar.Date).Amount;
                effectiveMarketDates.Add(priceBar.Date);
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
            {
                priceMissing = true;
            }

            var unrealizedPnlEur = priceMissing ? 0m : marketValueEur - costBasisEur;
            var unrealizedPnlPct = priceMissing || costBasisEur == 0m ? 0m : unrealizedPnlEur / costBasisEur;

            positionSnapshots.Add(new PortfolioPositionSnapshot(
                instrumentLots.Key.AccountId,
                instrument.InstrumentId,
                instrument.Symbol,
                string.IsNullOrWhiteSpace(instrument.ISIN) ? null : instrument.ISIN,
                instrumentLots.Key.Direction,
                quantity,
                closePrice,
                priceCurrency,
                marketValueEur,
                costBasisEur,
                avgBuyEur,
                avgBuyNative,
                nativeCurrency,
                unrealizedPnlEur,
                unrealizedPnlPct,
                instrument.Type,
                providerSymbol,
                effectivePriceDate,
                priceMissing));
        }

        var cashSnapshots = new List<PortfolioCashSnapshot>();
        foreach (var entry in cashByCurrency.OrderBy(x => x.Key))
        {
            var currency = Enum.Parse<WealthIQ.Domain.Enumeration.Currency>(entry.Key, true);
            // Cash conversion is resilient too: a missing FX rate must not blank the page.
            try
            {
                var amountInBase = _fxConverter.Convert(new Money(entry.Value, currency), valuationDate);
                cashSnapshots.Add(new PortfolioCashSnapshot(entry.Key, entry.Value, amountInBase.Amount));
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
            {
                cashSnapshots.Add(new PortfolioCashSnapshot(entry.Key, entry.Value, 0m));
            }
        }
```

The lines below this block (`var effectiveMarketDate = ...; var total = ...; return new PortfolioValuationSnapshot(...)`) stay as they are.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~PortfolioValuationServiceTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Build the whole solution to catch ripple effects**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded. (Nothing else constructs `PortfolioPositionSnapshot`, so there should be no other breaks.)

- [ ] **Step 7: Commit**

```bash
git add src/WealthIQ.Application/Valuation/ tests/WealthIQ.Tests/Application/Valuation/PortfolioValuationServiceTests.cs
git commit -m "feat: extend valuation snapshot with cost basis, avg buy price, unrealized P&L; resilient pricing"
```

---

## Task 2: Dashboard contracts & core composition (holdings, ISIN rollup, allocation)

**Files:**
- Create: `src/WealthIQ.Application/Dashboard/DashboardContracts.cs`
- Create: `src/WealthIQ.Application/Dashboard/PortfolioDashboardService.cs`
- Test: `tests/WealthIQ.Tests/Application/Dashboard/PortfolioDashboardServiceTests.cs`

- [ ] **Step 1: Create the contract records**

Create `src/WealthIQ.Application/Dashboard/DashboardContracts.cs`:

```csharp
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.Dashboard;

/// <summary>One holding row, grouped by ISIN (within an account, or across all accounts for the "Alle" view).
/// Market value / P&L are null when any underlying position lacks a usable price (resilient display).</summary>
public sealed record DashboardHolding(
    string? Isin,
    string Symbol,
    string Name,
    string AssetClass,
    decimal Quantity,
    decimal AverageBuyPriceInBaseCurrency,
    decimal? AverageBuyPriceNative,
    CurrencyCode? NativeCurrency,
    decimal? ClosePrice,
    CurrencyCode? PriceCurrency,
    decimal CostBasisInBaseCurrency,
    decimal? MarketValueInBaseCurrency,
    decimal? UnrealizedPnlInBaseCurrency,
    decimal? UnrealizedPnlPct,
    string? ProviderSymbol,
    bool PriceMissing);

public sealed record DashboardAllocationSlice(string AssetClass, decimal ValueInBaseCurrency, decimal Percent);

public sealed record DashboardKpis(
    decimal TotalSecuritiesValueInBaseCurrency,
    decimal UnrealizedPnlInBaseCurrency,
    decimal UnrealizedPnlPct,
    decimal DividendsYtdInBaseCurrency,
    decimal RealizedYtdInBaseCurrency,
    int PositionCount,
    int AccountCount,
    int PriceMissingCount);

/// <summary>One selectable view: either a single account or the combined "Alle" view.</summary>
public sealed record DashboardView(
    string AccountKey,            // "ALL" or the account id (Guid string)
    string AccountLabel,          // "Alle Konten" or the account number
    IReadOnlyList<DashboardHolding> Holdings,
    IReadOnlyList<DashboardAllocationSlice> Allocation,
    DashboardKpis Kpis);

public sealed record PortfolioDashboardReport(
    DateOnly ValuationDate,
    DateOnly EffectivePriceDate,
    IReadOnlyList<DashboardView> Views);
```

- [ ] **Step 2: Write the failing test (holdings + rollup + allocation)**

Create `tests/WealthIQ.Tests/Application/Dashboard/PortfolioDashboardServiceTests.cs`. This file also defines the stubs reused by Task 3's tests.

```csharp
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Dashboard;
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Application.Valuation;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Application.Dashboard;

public sealed class PortfolioDashboardServiceTests
{
    private static readonly DateOnly Today = new(2025, 5, 4);

    [Fact]
    public async Task GenerateAsync_TwoAccountsSameIsin_RollsUpAllViewByIsin()
    {
        var accountA = AccountId.NewId();
        var accountB = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "IE00TEST1234", "TEST", "Test ETF", 0.30m) { Type = "ETF_EQUITY" };

        var ledger = new PortfolioLedger(
            [
                Buy(accountA, instrumentId, "BUY-A", new DateOnly(2025, 5, 1), 2m, 100m),
                Buy(accountB, instrumentId, "BUY-B", new DateOnly(2025, 5, 1), 3m, 110m),
            ],
            [instrument],
            [new Account(accountA, "ACC-A"), new Account(accountB, "ACC-B")]);

        var service = BuildService(ledger, closePrice: 120m);

        var report = await service.GenerateAsync(Today);

        // Views: ALL first, then the two accounts.
        Assert.Equal("ALL", report.Views[0].AccountKey);
        var all = report.Views[0];
        var holding = Assert.Single(all.Holdings);
        Assert.Equal(5m, holding.Quantity);                 // 2 + 3
        Assert.Equal(560m, holding.CostBasisInBaseCurrency); // 2*100 + 3*110
        Assert.Equal(112m, holding.AverageBuyPriceInBaseCurrency); // 560 / 5
        Assert.Equal(600m, holding.MarketValueInBaseCurrency);     // 5 * 120
        Assert.Equal(40m, holding.UnrealizedPnlInBaseCurrency);
        Assert.Equal(600m, all.Kpis.TotalSecuritiesValueInBaseCurrency);
        Assert.Equal(2, all.Kpis.AccountCount);

        // Per-account view exists with only its own holding.
        var accA = report.Views.Single(v => v.AccountLabel == "ACC-A");
        Assert.Equal(200m, accA.Holdings.Single().CostBasisInBaseCurrency);
    }

    [Fact]
    public async Task GenerateAsync_Allocation_GroupsByAssetClassExcludingMissingPrice()
    {
        var account = AccountId.NewId();
        var etf = InstrumentId.NewId();
        var gold = InstrumentId.NewId();
        var instruments = new[]
        {
            new Instrument(etf, "IE00ETF00001", "ETF", "Equity ETF", 0.30m) { Type = "ETF_EQUITY" },
            new Instrument(gold, "DE000GOLD001", "GOLD", "Gold ETC", 0m) { Type = "ETC_GOLD" },
        };
        var ledger = new PortfolioLedger(
            [
                Buy(account, etf, "BUY-ETF", new DateOnly(2025, 5, 1), 1m, 100m),
                Buy(account, gold, "BUY-GOLD", new DateOnly(2025, 5, 1), 1m, 50m),
            ],
            instruments,
            [new Account(account, "ACC")]);

        // Gold has no market-data mapping → price missing → excluded from allocation/total.
        var service = BuildService(ledger, closePrice: 100m, missingSymbolForIsin: "DE000GOLD001");

        var report = await service.GenerateAsync(Today);
        var all = report.Views[0];

        var slice = Assert.Single(all.Allocation);
        Assert.Equal("ETF_EQUITY", slice.AssetClass);
        Assert.Equal(100m, slice.ValueInBaseCurrency);
        Assert.Equal(100m, slice.Percent);
        Assert.Equal(1, all.Kpis.PriceMissingCount);
        Assert.Equal(100m, all.Kpis.TotalSecuritiesValueInBaseCurrency);
    }

    // ---- helpers / stubs (shared with Task 3 tests) ----

    internal static TradeEntry Buy(AccountId account, InstrumentId instrument, string reference, DateOnly date, decimal qty, decimal price)
        => new(
            PortfolioEntryId.NewId(), account,
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), date,
            Provenance(reference), instrument, TradeSide.Buy,
            new Quantity(qty), new Money(price, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR));

    internal static SourceProvenance Provenance(string reference) => new()
    {
        SourceSystem = "TEST",
        ImportFormat = "TEST",
        SourceLocation = "unit-test",
        SourceRecordReference = reference
    };

    internal static PortfolioDashboardService BuildService(PortfolioLedger ledger, decimal closePrice, string? missingSymbolForIsin = null)
    {
        var valuation = new PortfolioValuationService(
            new StubPriceLookup(closePrice),
            new StubMarketDataMap(missingSymbolForIsin),
            new StubFx());
        return new PortfolioDashboardService(
            new StubLedgerStore(ledger),
            new InstrumentCatalogBuilder(new PassthroughEnricher()),
            valuation,
            new StubFx());
    }

    private sealed class StubLedgerStore(PortfolioLedger ledger) : ILedgerStore
    {
        public Task<PortfolioLedger> LoadLedgerAsync(CancellationToken ct = default) => Task.FromResult(ledger);
        public Task<LedgerSaveResult> SaveLedgerAsync(PortfolioLedger ledger, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class PassthroughEnricher : IInstrumentProfileEnricher
    {
        public Instrument Enrich(Instrument instrument) => instrument; // Type already set on the test instruments
    }

    private sealed class StubPriceLookup(decimal close) : IHistoricalPriceLookup
    {
        public PriceBar GetPriceBar(DateOnly pricingDate, string providerSymbol, PriceLookupDateHandling dateHandling = PriceLookupDateHandling.LatestOnOrBefore)
            => new(new DateOnly(2025, 5, 2), providerSymbol, CurrencyCode.EUR, close, close, close, close, close, 1000);
    }

    private sealed class StubMarketDataMap(string? missingIsin) : IInstrumentMarketDataMap
    {
        public InstrumentMarketDataProfile GetProfile(string isin, CurrencyCode currency)
            => isin == missingIsin
                ? throw new InvalidOperationException($"No mapping for {isin}")
                : new InstrumentMarketDataProfile("YahooFinance", isin + ".TEST");
    }

    private sealed class StubFx : IFxRateLookup
    {
        public decimal GetRate(DateOnly conversionDate, CurrencyCode sourceCurrency, CurrencyCode targetCurrency, FxRateLookupDateHandling dateHandling = FxRateLookupDateHandling.ExactDate)
            => sourceCurrency == targetCurrency ? 1m : throw new InvalidOperationException("Unexpected FX conversion in dashboard test.");
    }
}
```

> Note: verified signatures used here — `Account(AccountId AccountId, string AccountNumber)` and `PortfolioLedger(IReadOnlyList<PortfolioEntry> entries, IReadOnlyList<Instrument>? instruments = null, IReadOnlyList<Account>? accounts = null)`.

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~PortfolioDashboardServiceTests"`
Expected: FAIL — `PortfolioDashboardService` does not exist yet.

- [ ] **Step 4: Implement the service core (holdings + rollup + allocation; KPIs come in Task 3)**

Create `src/WealthIQ.Application/Dashboard/PortfolioDashboardService.cs`:

```csharp
using WealthIQ.Application.Currency;
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Valuation;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.Dashboard;

/// <summary>Builds the "Mein Portfolio" dashboard report: per-account holdings grouped by ISIN,
/// a combined "Alle" rollup (EUR), asset-class allocation, and YTD KPIs. Display-only and resilient —
/// a missing price/FX flags a row instead of failing the whole report (unlike the tax engine).</summary>
public sealed class PortfolioDashboardService(
    ILedgerStore ledgerStore,
    InstrumentCatalogBuilder catalogBuilder,
    PortfolioValuationService valuationService,
    IFxRateLookup fxRateLookup)
{
    private const string AllKey = "ALL";
    private readonly FxConverter _fxConverter = new(fxRateLookup, CurrencyCode.EUR);

    public async Task<PortfolioDashboardReport> GenerateAsync(DateOnly today, CancellationToken ct = default)
    {
        var ledger = await ledgerStore.LoadLedgerAsync(ct);
        var catalog = catalogBuilder.Build(ledger.Instruments);
        var instrumentById = catalog.ToDictionary(x => x.InstrumentId);

        var valuation = valuationService.Calculate(ledger, catalog, today);

        var accountNumbers = ledger.Accounts.ToDictionary(a => a.AccountId, a => a.AccountNumber);
        string LabelFor(AccountId id) => accountNumbers.TryGetValue(id, out var n) ? n : id.ToString();

        var dividendsByAccount = DividendsYtdByAccount(ledger, today.Year);
        var realizedByAccount = RealizedYtdByAccount(ledger, today.Year);

        var views = new List<DashboardView>();

        // "Alle" view: all positions rolled up by ISIN across accounts.
        views.Add(BuildView(
            AllKey, "Alle Konten",
            valuation.Positions, instrumentById,
            dividendsByAccount.Values.Sum(), realizedByAccount.Values.Sum(),
            accountCount: ledger.Accounts.Count));

        // Per-account views (only accounts that actually hold positions), ordered by account number.
        foreach (var accountGroup in valuation.Positions
                     .GroupBy(p => p.AccountId)
                     .OrderBy(g => LabelFor(g.Key), StringComparer.Ordinal))
        {
            views.Add(BuildView(
                accountGroup.Key.Value.ToString(), LabelFor(accountGroup.Key),
                accountGroup.ToList(), instrumentById,
                dividendsByAccount.GetValueOrDefault(accountGroup.Key),
                realizedByAccount.GetValueOrDefault(accountGroup.Key),
                accountCount: 1));
        }

        return new PortfolioDashboardReport(today, valuation.EffectiveMarketDate, views);
    }

    private DashboardView BuildView(
        string accountKey, string accountLabel,
        IReadOnlyList<PortfolioPositionSnapshot> positions,
        IReadOnlyDictionary<InstrumentId, Instrument> instrumentById,
        decimal dividendsYtd, decimal realizedYtd, int accountCount)
    {
        var holdings = positions
            .GroupBy(p => p.InstrumentId)
            .Select(g => BuildHolding(g.ToList(), instrumentById[g.Key]))
            .OrderByDescending(h => h.MarketValueInBaseCurrency ?? 0m)
            .ThenBy(h => h.Symbol, StringComparer.Ordinal)
            .ToList();

        var priced = holdings.Where(h => !h.PriceMissing).ToList();
        var totalValue = priced.Sum(h => h.MarketValueInBaseCurrency ?? 0m);
        var totalCost = priced.Sum(h => h.CostBasisInBaseCurrency);
        var unrealized = priced.Sum(h => h.UnrealizedPnlInBaseCurrency ?? 0m);
        var unrealizedPct = totalCost == 0m ? 0m : unrealized / totalCost;

        var allocation = priced
            .GroupBy(h => string.IsNullOrWhiteSpace(h.AssetClass) ? "Sonstige" : h.AssetClass)
            .Select(g => new { AssetClass = g.Key, Value = g.Sum(h => h.MarketValueInBaseCurrency ?? 0m) })
            .Where(x => x.Value > 0m)
            .OrderByDescending(x => x.Value)
            .Select(x => new DashboardAllocationSlice(
                x.AssetClass, x.Value, totalValue == 0m ? 0m : Math.Round(x.Value / totalValue * 100m, 2)))
            .ToList();

        var kpis = new DashboardKpis(
            totalValue, unrealized, unrealizedPct,
            dividendsYtd, realizedYtd,
            PositionCount: holdings.Count, AccountCount: accountCount,
            PriceMissingCount: holdings.Count(h => h.PriceMissing));

        return new DashboardView(accountKey, accountLabel, holdings, allocation, kpis);
    }

    private static DashboardHolding BuildHolding(IReadOnlyList<PortfolioPositionSnapshot> group, Instrument instrument)
    {
        var quantity = group.Sum(x => x.Quantity);
        var costBasis = group.Sum(x => x.CostBasisInBaseCurrency);
        var anyMissing = group.Any(x => x.PriceMissing);

        var sameCurrency = group.Select(x => x.NativeCurrency).Distinct().Count() == 1;
        CurrencyCode? nativeCurrency = sameCurrency ? group[0].NativeCurrency : null;
        decimal? avgBuyNative = sameCurrency && quantity != 0m && group.All(x => x.AverageBuyPriceNative.HasValue)
            ? group.Sum(x => x.AverageBuyPriceNative!.Value * x.Quantity) / quantity
            : null;
        var avgBuyEur = quantity != 0m ? costBasis / quantity : 0m;

        decimal? marketValue = anyMissing ? null : group.Sum(x => x.MarketValueInBaseCurrency);
        decimal? pnl = anyMissing ? null : marketValue - costBasis;
        decimal? pnlPct = anyMissing || costBasis == 0m ? null : pnl / costBasis;
        decimal? closePrice = anyMissing || !sameCurrency ? null : group[0].ClosePrice;
        CurrencyCode? priceCurrency = anyMissing || !sameCurrency ? null : group[0].PriceCurrency;
        var providerSymbol = group.FirstOrDefault(x => x.ProviderSymbol is not null)?.ProviderSymbol;

        return new DashboardHolding(
            string.IsNullOrWhiteSpace(instrument.ISIN) ? null : instrument.ISIN,
            instrument.Symbol,
            instrument.Name,
            string.IsNullOrWhiteSpace(instrument.Type) ? "Sonstige" : instrument.Type,
            quantity,
            avgBuyEur,
            avgBuyNative,
            nativeCurrency,
            closePrice,
            priceCurrency,
            costBasis,
            marketValue,
            pnl,
            pnlPct,
            providerSymbol,
            anyMissing);
    }

    // Implemented in Task 3.
    private Dictionary<AccountId, decimal> DividendsYtdByAccount(PortfolioLedger ledger, int year)
        => new();

    // Implemented in Task 3.
    private Dictionary<AccountId, decimal> RealizedYtdByAccount(PortfolioLedger ledger, int year)
        => new();
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~PortfolioDashboardServiceTests"`
Expected: PASS (2 tests). (KPI dividend/realized values are 0 for now — Task 3 fills them and adds their tests.)

- [ ] **Step 6: Commit**

```bash
git add src/WealthIQ.Application/Dashboard/ tests/WealthIQ.Tests/Application/Dashboard/
git commit -m "feat: add PortfolioDashboardService core (holdings, ISIN rollup, allocation)"
```

---

## Task 3: Dashboard KPIs — dividends YTD & realized YTD

**Files:**
- Modify: `src/WealthIQ.Application/Dashboard/PortfolioDashboardService.cs`
- Test: `tests/WealthIQ.Tests/Application/Dashboard/PortfolioDashboardServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these two tests inside `PortfolioDashboardServiceTests` (after the existing facts, before the helpers):

```csharp
    [Fact]
    public async Task GenerateAsync_DividendsThisYear_AreSummedInBaseCurrency()
    {
        var account = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "IE00TEST1234", "TEST", "Test ETF", 0.30m) { Type = "ETF_EQUITY" };

        var ledger = new PortfolioLedger(
            [
                Buy(account, instrumentId, "BUY-1", new DateOnly(2025, 1, 2), 10m, 100m),
                Dividend(account, instrumentId, "DIV-1", new DateOnly(2025, 3, 1), 40m),
                Dividend(account, instrumentId, "DIV-OLD", new DateOnly(2024, 3, 1), 999m), // prior year, excluded
            ],
            [instrument],
            [new Account(account, "ACC")]);

        var report = await BuildService(ledger, closePrice: 100m).GenerateAsync(Today);

        Assert.Equal(40m, report.Views[0].Kpis.DividendsYtdInBaseCurrency);
    }

    [Fact]
    public async Task GenerateAsync_RealizedThisYear_IsProceedsMinusCost()
    {
        var account = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "IE00TEST1234", "TEST", "Test ETF", 0.30m) { Type = "ETF_EQUITY" };

        var ledger = new PortfolioLedger(
            [
                Buy(account, instrumentId, "BUY-1", new DateOnly(2025, 1, 2), 10m, 100m),
                Sell(account, instrumentId, "SELL-1", new DateOnly(2025, 4, 1), 4m, 130m),
            ],
            [instrument],
            [new Account(account, "ACC")]);

        var report = await BuildService(ledger, closePrice: 120m).GenerateAsync(Today);

        // Realized = 4 * (130 - 100) = 120.
        Assert.Equal(120m, report.Views[0].Kpis.RealizedYtdInBaseCurrency);
    }
```

Also add a `Sell` and a `Dividend` factory helper next to the existing `Buy` helper:

```csharp
    internal static TradeEntry Sell(AccountId account, InstrumentId instrument, string reference, DateOnly date, decimal qty, decimal price)
        => new(
            PortfolioEntryId.NewId(), account,
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), date,
            Provenance(reference), instrument, TradeSide.Sell,
            new Quantity(qty), new Money(price, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR));

    internal static CashEntry Dividend(AccountId account, InstrumentId instrument, string reference, DateOnly date, decimal amount)
        => new(
            PortfolioEntryId.NewId(), account,
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), date,
            Provenance(reference), instrument, Domain.Enumeration.CashFlowType.Dividend,
            new Money(amount, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~PortfolioDashboardServiceTests"`
Expected: FAIL — dividends/realized return 0 (the placeholder implementations).

- [ ] **Step 3: Implement the two KPI methods**

Replace the two placeholder methods at the bottom of `PortfolioDashboardService.cs` with:

```csharp
    private Dictionary<AccountId, decimal> DividendsYtdByAccount(PortfolioLedger ledger, int year)
    {
        var result = new Dictionary<AccountId, decimal>();
        foreach (var cash in ledger.Entries.OfType<CashEntry>()
                     .Where(c => c.CashFlowType == Domain.Enumeration.CashFlowType.Dividend && c.EffectiveDate.Year == year))
        {
            try
            {
                var eur = _fxConverter.Convert(cash.GrossAmount, cash.EffectiveDate).Amount;
                result[cash.AccountId] = result.GetValueOrDefault(cash.AccountId) + eur;
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
            {
                // Missing FX must not blank the dashboard — skip this entry's contribution.
            }
        }
        return result;
    }

    private Dictionary<AccountId, decimal> RealizedYtdByAccount(PortfolioLedger ledger, int year)
    {
        var matcher = new Matcher.FiFoMatcher();
        var openLots = new List<Domain.Model.Lot.OpenLot>();
        var result = new Dictionary<AccountId, decimal>();

        foreach (var trade in ledger.Entries.OfType<TradeEntry>().OrderBy(x => x.OccurredAt))
        {
            var match = matcher.Match(trade, openLots, Domain.Enumeration.LotMatchingPolicy.FIFO);
            openLots = match.UpdatedOpenLots.ToList();
            if (match.NewlyOpenedRemainderLot is not null)
            {
                openLots.Add(match.NewlyOpenedRemainderLot);
            }

            foreach (var c in match.Consumptions.Where(c => c.CloseTradeDate.Year == year))
            {
                try
                {
                    var proceedsNative = new Money(
                        c.CloseUnitPrice.Amount * c.MatchedQuantity.Value - c.AllocatedCloseFees.Amount,
                        c.CloseUnitPrice.Currency);
                    var costNative = new Money(
                        c.OpenUnitPrice.Amount * c.MatchedQuantity.Value + c.AllocatedOpenFees.Amount,
                        c.OpenUnitPrice.Currency);
                    var realizedEur = _fxConverter.Convert(proceedsNative, c.CloseTradeDate).Amount
                                      - _fxConverter.Convert(costNative, c.OpenTradeDate).Amount;
                    result[c.AccountId] = result.GetValueOrDefault(c.AccountId) + realizedEur;
                }
                catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
                {
                    // Missing FX — skip this consumption's contribution.
                }
            }
        }
        return result;
    }
```

> Verified: `LotMatchingPolicy` lives in `WealthIQ.Domain.Enumeration` (so `Domain.Enumeration.LotMatchingPolicy.FIFO` resolves). `FiFoMatcher` is in `WealthIQ.Application.Matcher`; `OpenLot` in `WealthIQ.Domain.Model.Lot`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~PortfolioDashboardServiceTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/Dashboard/PortfolioDashboardService.cs tests/WealthIQ.Tests/Application/Dashboard/PortfolioDashboardServiceTests.cs
git commit -m "feat: add dividends-YTD and realized-YTD KPIs to dashboard service"
```

---

## Task 4: Register services in DI

**Files:**
- Modify: `src/WealthIQ.Web/Program.cs`

- [ ] **Step 1: Add registrations**

In `Program.cs`, in the `// --- Tax replay ---` region (around line 136–138), add the dashboard services immediately after `builder.Services.AddScoped<AnnualTaxReportService>();`:

```csharp
// --- Portfolio dashboard ---
builder.Services.AddScoped<WealthIQ.Application.Valuation.PortfolioValuationService>();
builder.Services.AddScoped<WealthIQ.Application.Dashboard.PortfolioDashboardService>();
```

- [ ] **Step 2: Build to verify it compiles and DI resolves at startup**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Web/Program.cs
git commit -m "chore: register PortfolioValuationService and PortfolioDashboardService in DI"
```

---

## Task 5: Reusable component tweaks (StatCard ValueText, LightweightChart reference line)

**Files:**
- Modify: `src/WealthIQ.Web/Components/Shared/StatCard.razor`
- Modify: `src/WealthIQ.Web/Components/Shared/LightweightChart.razor`
- Modify: `src/WealthIQ.Web/wwwroot/wiq-charts.js`

- [ ] **Step 1: Add an optional `ValueText` override to StatCard**

In `StatCard.razor`, replace the figure `<div>` (lines 6–15) with a version that prefers `ValueText` when supplied:

```razor
    <div class="wiq-figure @FigureClass" style="font-size:1.6rem;margin-top:6px;">
        @if (!string.IsNullOrWhiteSpace(ValueText))
        {
            @ValueText
        }
        else if (CountUp)
        {
            <span class="wiq-countup" data-target="@Value.ToString(System.Globalization.CultureInfo.InvariantCulture)" data-suffix=" €">@Display</span>
        }
        else
        {
            @Display
        }
    </div>
```

Then add the parameter to the `@code` block (after the existing `CountUp` parameter):

```csharp
    /// <summary>When set, rendered verbatim instead of the "N2 €" formatting of <see cref="Value"/>.
    /// Use for non-currency figures (counts, percentages).</summary>
    [Parameter] public string? ValueText { get; set; }
```

- [ ] **Step 2: Add a reference-price line to the chart JS**

In `wiq-charts.js`, add a `setReferenceLine` function to the `window.wiqCharts` object (insert after the `setData` function, before `applyTheme`):

```javascript
    setReferenceLine: function (id, price, theme) {
        var entry = this._charts[id];
        if (!entry) return;
        if (entry.refLine) { try { entry.series.removePriceLine(entry.refLine); } catch (e) { } entry.refLine = null; }
        if (price === null || price === undefined) return;
        entry.refLine = entry.series.createPriceLine({
            price: price,
            color: theme.textColor,
            lineWidth: 1,
            lineStyle: 2, // dashed
            axisLabelVisible: true,
            title: 'Ø Kauf'
        });
    },
```

- [ ] **Step 3: Add a `ReferencePrice` parameter to LightweightChart and push it after data**

In `LightweightChart.razor`, add the parameter after `InitialRangeDays`:

```csharp
    /// <summary>Optional dashed horizontal reference line (e.g. average buy price) in the series' price units.</summary>
    [Parameter] public decimal? ReferencePrice { get; set; }
```

Then, in `PushDataAsync()`, after the `if (Kind == "line") { ... } else { ... }` block (i.e. as the last statement of the method), add:

```csharp
        await JS.InvokeVoidAsync("wiqCharts.setReferenceLine", _id, ReferencePrice, Theme);
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded. (JS changes aren't compiled; they're verified in the manual smoke test in Task 8.)

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Web/Components/Shared/StatCard.razor src/WealthIQ.Web/Components/Shared/LightweightChart.razor src/WealthIQ.Web/wwwroot/wiq-charts.js
git commit -m "feat: StatCard ValueText override + LightweightChart reference-price line"
```

---

## Task 6: Re-route Steuerreport and add the Portfolio nav entry

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Steuerreport.razor`
- Modify: `src/WealthIQ.Web/Components/Layout/MainLayout.razor`

- [ ] **Step 1: Move Steuerreport off the root route**

In `Steuerreport.razor`, change line 1 from:

```razor
@page "/"
```

to:

```razor
@page "/steuerreport"
```

- [ ] **Step 2: Update the navigation drawer**

In `MainLayout.razor`, replace the `Bericht` block (lines 23–24) with a Portfolio entry plus the re-routed Steuerreport:

```razor
                <div class="wiq-nav-label">Portfolio</div>
                <MudNavLink Href="/" Match="NavLinkMatch.All" Icon="@Icons.Material.Outlined.Dashboard">Mein Portfolio</MudNavLink>

                <div class="wiq-nav-label">Bericht</div>
                <MudNavLink Href="/steuerreport" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Outlined.ReceiptLong">Steuerreport</MudNavLink>
```

- [ ] **Step 3: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded. (`/` now has no page until Task 7 — the app still builds; visiting `/` would 404 until the Dashboard page exists. Task 7 adds it before any smoke test.)

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Steuerreport.razor src/WealthIQ.Web/Components/Layout/MainLayout.razor
git commit -m "refactor: move Steuerreport to /steuerreport, add Portfolio nav entry"
```

---

## Task 7: Dashboard page — header, KPIs, allocation donut, holdings table, refresh

**Files:**
- Create: `src/WealthIQ.Web/Components/Pages/Dashboard/Dashboard.razor`

This task builds everything except the chart panel (Task 8 adds the chart into the placeholder column).

- [ ] **Step 1: Create the page**

Create `src/WealthIQ.Web/Components/Pages/Dashboard/Dashboard.razor`:

```razor
@page "/"
@using MudBlazor.Charts
@using WealthIQ.Application.Dashboard
@inject IServiceScopeFactory ScopeFactory
@inject ISnackbar Snackbar
@inject IJSRuntime JS

<PageTitle>WealthIQ — Mein Portfolio</PageTitle>

<PageHeader Title="Mein Portfolio" Subtitle="Aktuelle Bestände, Wert und Entwicklung seit Kauf">
    <Actions>
        @if (_report is not null && _report.Views.Count > 1)
        {
            <MudSelect T="string" Value="_selectedKey" ValueChanged="OnViewChanged" Label="Konto"
                       Variant="Variant.Outlined" Dense="true" Style="min-width:180px;" Class="me-2">
                @foreach (var view in _report.Views)
                {
                    <MudSelectItem T="string" Value="@view.AccountKey">@view.AccountLabel</MudSelectItem>
                }
            </MudSelect>
        }
        <MudButton Variant="Variant.Outlined" Color="Color.Primary" StartIcon="@Icons.Material.Outlined.Refresh"
                   OnClick="RefreshPricesAsync" Disabled="_refreshing" Class="me-2">
            @(_refreshing ? "Aktualisiere…" : "Kurse aktualisieren")
        </MudButton>
        @if (_report is not null)
        {
            <MudChip T="string" Variant="Variant.Text" Color="@(IsStale ? Color.Warning : Color.Default)" Size="Size.Small">
                Kurse per @_report.EffectivePriceDate.ToString("dd.MM.yyyy")
            </MudChip>
        }
    </Actions>
</PageHeader>

@if (_error is not null)
{
    <MudAlert Severity="Severity.Error" Class="mb-4">@_error</MudAlert>
}

@if (_loading)
{
    <div style="display:flex;justify-content:center;padding:64px;">
        <MudProgressCircular Indeterminate="true" Color="Color.Primary" />
    </div>
}
else if (Current is null || Current.Holdings.Count == 0)
{
    <SectionCard>
        <MudAlert Severity="Severity.Info" Variant="Variant.Text">
            Noch keine Bestände. Importiere zuerst ein Broker-Statement auf der Import-Seite.
        </MudAlert>
    </SectionCard>
}
else
{
    @* Hero: allocation donut + KPI cards. *@
    <MudGrid Class="mb-4 wiq-rise" @key="@($"hero-{_selectedKey}")">
        <MudItem xs="12" md="5">
            <MudPaper Elevation="0" Class="wiq-card" Style="height:100%;padding:24px;display:flex;flex-direction:column;align-items:center;justify-content:center;">
                <MudText Typo="Typo.overline" Style="color:var(--mud-palette-text-secondary);align-self:flex-start;">Allokation · Anlageklasse</MudText>
                @if (Current.Allocation.Count > 0)
                {
                    <MudChart T="double" ChartType="ChartType.Donut" Width="220px" Height="220px"
                              ChartSeries="@AllocationSeries" ChartLabels="@AllocationLabels"
                              ChartOptions="@_chartOptions" />
                }
                else
                {
                    <MudText Typo="Typo.body2" Style="color:var(--mud-palette-text-secondary);padding:32px 0;">Keine bewerteten Bestände.</MudText>
                }
            </MudPaper>
        </MudItem>
        <MudItem xs="12" md="7">
            <MudGrid @key="@($"kpi-{_selectedKey}")">
                <MudItem xs="12" sm="6"><StatCard Caption="Gesamtwert (Wertpapiere)" Value="@Current.Kpis.TotalSecuritiesValueInBaseCurrency" Accent="true" CountUp="true" /></MudItem>
                <MudItem xs="12" sm="6"><StatCard Caption="Nicht realisierter G/V" Value="@Current.Kpis.UnrealizedPnlInBaseCurrency" Hint="@PctHint(Current.Kpis.UnrealizedPnlPct)" CountUp="true" /></MudItem>
                <MudItem xs="12" sm="6"><StatCard Caption="@($"Dividenden {_report!.ValuationDate.Year}")" Value="@Current.Kpis.DividendsYtdInBaseCurrency" CountUp="true" /></MudItem>
                <MudItem xs="12" sm="6"><StatCard Caption="@($"Realisiert {_report!.ValuationDate.Year}")" Value="@Current.Kpis.RealizedYtdInBaseCurrency" CountUp="true" /></MudItem>
                <MudItem xs="12" sm="6"><StatCard Caption="Positionen" ValueText="@Current.Kpis.PositionCount.ToString()" Hint="@AccountHint(Current.Kpis.AccountCount)" /></MudItem>
                @if (Current.Kpis.PriceMissingCount > 0)
                {
                    <MudItem xs="12" sm="6"><StatCard Caption="Ohne Kurs" ValueText="@Current.Kpis.PriceMissingCount.ToString()" Hint="aus Summe ausgeschlossen" /></MudItem>
                }
            </MudGrid>
        </MudItem>
    </MudGrid>

    @* Master–detail: holdings table (left) + chart placeholder (right; filled in Task 8). *@
    <MudGrid class="wiq-rise-2">
        <MudItem xs="12" md="7">
            <SectionCard>
                <ChildContent>
                    <MudText Typo="Typo.overline" Style="color:var(--mud-palette-text-secondary);">Positionen (gruppiert nach ISIN)</MudText>
                    <MudTable Items="Current.Holdings" Dense="true" Hover="true" Breakpoint="Breakpoint.Sm"
                              T="DashboardHolding" OnRowClick="OnHoldingRowClick" RowClass="cursor-pointer">
                        <HeaderContent>
                            <MudTh>Instrument</MudTh>
                            <MudTh Style="text-align:right">Menge</MudTh>
                            <MudTh Style="text-align:right">Ø Kauf</MudTh>
                            <MudTh Style="text-align:right">Kurs</MudTh>
                            <MudTh Style="text-align:right">Wert €</MudTh>
                            <MudTh Style="text-align:right">+/− €</MudTh>
                            <MudTh Style="text-align:right">+/− %</MudTh>
                        </HeaderContent>
                        <RowTemplate Context="h">
                            <MudTd DataLabel="Instrument">
                                <div style="font-weight:600;">@h.Symbol</div>
                                <div style="font-size:.75rem;color:var(--mud-palette-text-secondary);">@h.Isin</div>
                            </MudTd>
                            <MudTd DataLabel="Menge" Style="text-align:right">@h.Quantity.ToString("0.####")</MudTd>
                            <MudTd DataLabel="Ø Kauf" Style="text-align:right">@AvgBuyText(h)</MudTd>
                            <MudTd DataLabel="Kurs" Style="text-align:right">@PriceText(h)</MudTd>
                            <MudTd DataLabel="Wert €" Style="text-align:right">@EurOrDash(h.MarketValueInBaseCurrency)</MudTd>
                            <MudTd DataLabel="+/− €" Style="@PnlStyle(h.UnrealizedPnlInBaseCurrency)">@EurOrDash(h.UnrealizedPnlInBaseCurrency)</MudTd>
                            <MudTd DataLabel="+/− %" Style="@PnlStyle(h.UnrealizedPnlInBaseCurrency)">@PctOrDash(h.UnrealizedPnlPct)</MudTd>
                        </RowTemplate>
                    </MudTable>
                </ChildContent>
            </SectionCard>
        </MudItem>
        <MudItem xs="12" md="5">
            <SectionCard>
                <ChildContent>
                    <MudText Typo="Typo.overline" Style="color:var(--mud-palette-text-secondary);">Kursverlauf</MudText>
                    <MudText Typo="Typo.body2" Style="color:var(--mud-palette-text-secondary);">Chart folgt in Task 8.</MudText>
                </ChildContent>
            </SectionCard>
        </MudItem>
    </MudGrid>
}

@code {
    private bool _loading = true;
    private bool _refreshing;
    private string? _error;
    private PortfolioDashboardReport? _report;
    private string _selectedKey = "ALL";

    private DashboardView? Current => _report?.Views.FirstOrDefault(v => v.AccountKey == _selectedKey) ?? _report?.Views.FirstOrDefault();
    private bool IsStale => _report is not null && _report.ValuationDate.DayNumber - _report.EffectivePriceDate.DayNumber > 4;

    private readonly DonutChartOptions _chartOptions = new()
    {
        ChartPalette = new[] { "#34D399", "#60A5FA", "#A78BFA", "#FBBF24", "#F472B6", "#22D3EE" },
        ShowLegend = true,
    };

    private List<ChartSeries<double>> AllocationSeries => new()
    {
        new ChartSeries<double>
        {
            Name = "Allokation",
            Data = (Current?.Allocation ?? Array.Empty<DashboardAllocationSlice>())
                .Select(s => Math.Round((double)s.ValueInBaseCurrency, 2)).ToArray(),
        }
    };

    private string[] AllocationLabels => (Current?.Allocation ?? Array.Empty<DashboardAllocationSlice>())
        .Select(s => s.AssetClass).ToArray();

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            // Fresh DI scope each load so the in-memory price/FX lookups reflect the latest DB state
            // (mirrors the "fresh context per scope" intent in Program.cs; important after a refresh).
            await using var scope = ScopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<PortfolioDashboardService>();
            _report = await service.GenerateAsync(DateOnly.FromDateTime(DateTime.Today));
            if (_report.Views.All(v => v.AccountKey != _selectedKey))
            {
                _selectedKey = _report.Views.FirstOrDefault()?.AccountKey ?? "ALL";
            }
        }
        catch (Exception ex)
        {
            _error = $"Portfolio konnte nicht geladen werden: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnViewChanged(string key)
    {
        _selectedKey = key;
    }

    private async Task RefreshPricesAsync()
    {
        _refreshing = true;
        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var refresh = scope.ServiceProvider.GetRequiredService<WealthIQ.Application.MarketData.HistoricalPriceRefreshService>();
            var result = await refresh.RefreshAsync(DateOnly.FromDateTime(DateTime.Today), forceFullReload: false, CancellationToken.None);
            if (result.HasBlockingDiagnostics)
            {
                Snackbar.Add("Kurse aktualisiert mit Fehlern — Details auf der Diagnose-Seite.", Severity.Warning);
            }
            else
            {
                Snackbar.Add($"Kurse aktualisiert: {result.Added} neu, {result.Updated} aktualisiert.", Severity.Success);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Aktualisierung fehlgeschlagen: {ex.Message}", Severity.Error);
        }
        finally
        {
            _refreshing = false;
        }
        await LoadAsync();
    }

    // Task 8 wires this to the chart; for now it is a no-op placeholder so the table is clickable.
    private void OnHoldingRowClick(TableRowClickEventArgs<DashboardHolding> args) { }

    private static string PctHint(decimal pct) => (pct >= 0 ? "+" : "") + (pct * 100m).ToString("N1", De) + " %";
    private static string AccountHint(int count) => count == 1 ? "1 Konto" : $"{count} Konten";

    private static string AvgBuyText(DashboardHolding h)
        => h.AverageBuyPriceNative is { } native && h.NativeCurrency is { } cur
            ? native.ToString("N2", De) + " " + Symbol(cur)
            : h.AverageBuyPriceInBaseCurrency.ToString("N2", De) + " €";

    private static string PriceText(DashboardHolding h)
        => h.PriceMissing || h.ClosePrice is null
            ? "Kurs fehlt"
            : h.ClosePrice.Value.ToString("N2", De) + (h.PriceCurrency is { } c ? " " + Symbol(c) : "");

    private static string EurOrDash(decimal? amount) => amount is null ? "—" : amount.Value.ToString("N2", De) + " €";
    private static string PctOrDash(decimal? pct) => pct is null ? "—" : (pct.Value >= 0 ? "+" : "") + (pct.Value * 100m).ToString("N1", De);
    private static string PnlStyle(decimal? pnl) => "text-align:right;color:" + (pnl is null ? "inherit" : pnl.Value >= 0 ? "#34D399" : "#F87171") + ";";

    private static string Symbol(WealthIQ.Domain.Enumeration.Currency c) => c switch
    {
        WealthIQ.Domain.Enumeration.Currency.EUR => "€",
        WealthIQ.Domain.Enumeration.Currency.USD => "$",
        WealthIQ.Domain.Enumeration.Currency.GBP => "£",
        _ => c.ToString()
    };

    private static readonly System.Globalization.CultureInfo De = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
}
```

> Note: verified — `DataRefreshResult(int Added, int Updated, int Skipped, IReadOnlyList<ImportDiagnostic> Diagnostics)` with a `HasBlockingDiagnostics` helper; `Currency` enum includes `USD, EUR, CHF, GBP, …`. The `Symbol` switch falls back to the enum name for any currency it doesn't special-case.

- [ ] **Step 2: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded.

- [ ] **Step 3: Manual smoke test**

Run: `dotnet run --project src/WealthIQ.Web`
Open the app at the printed URL. Verify:
- `/` shows "Mein Portfolio" with the donut, KPI cards, and holdings table (assuming data is imported).
- The account dropdown switches between "Alle Konten" and individual accounts and the table/KPIs change.
- The "Kurse aktualisieren" button runs and shows a snackbar; the "Kurse per …" chip shows a date.
- `/steuerreport` still renders the tax report; the nav shows Portfolio → Mein Portfolio and Bericht → Steuerreport.

Stop the app (Ctrl+C).

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Dashboard/Dashboard.razor
git commit -m "feat: portfolio dashboard page (KPIs, allocation donut, holdings, refresh)"
```

---

## Task 8: Master–detail chart panel

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Dashboard/Dashboard.razor`

Adds the chart: clicking a holding row loads that instrument's candles; a dropdown selects any instrument with price data (held or not); a held holding shows its average-buy reference line; a range toggle (1M/6M/1J) controls the initial window. Reuses `ChartSelectionState` for persistence.

- [ ] **Step 1: Inject chart dependencies and add usings**

At the top of `Dashboard.razor`, add after the existing `@inject` lines:

```razor
@using Microsoft.EntityFrameworkCore
@using WealthIQ.Application.MarketData
@using WealthIQ.Infrastructure.Persistence
@using WealthIQ.Web.Services
@inject ThemePreferenceService ThemePreference
@inject ChartSelectionState ChartSelection
@using CurrencyCode = WealthIQ.Domain.Enumeration.Currency
```

- [ ] **Step 2: Replace the chart placeholder column**

Replace the right-hand `<MudItem xs="12" md="5">…Chart folgt in Task 8.…</MudItem>` block (the one in the master–detail `MudGrid`) with:

```razor
        <MudItem xs="12" md="5">
            <SectionCard>
                <ChildContent>
                    <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;margin-bottom:8px;">
                        <MudText Typo="Typo.overline" Style="color:var(--mud-palette-text-secondary);">Kursverlauf</MudText>
                        <MudButtonGroup Size="Size.Small" Variant="Variant.Outlined">
                            <MudButton Color="@RangeColor(30)" OnClick="() => SetRange(30)">1M</MudButton>
                            <MudButton Color="@RangeColor(180)" OnClick="() => SetRange(180)">6M</MudButton>
                            <MudButton Color="@RangeColor(365)" OnClick="() => SetRange(365)">1J</MudButton>
                        </MudButtonGroup>
                    </div>
                    <MudSelect T="ChartInstrument" Value="_selectedChart" ValueChanged="OnChartInstrumentChanged"
                               ToStringFunc="o => o is null ? string.Empty : o.Label"
                               Label="Instrument" Variant="Variant.Outlined" Dense="true" Class="mb-2" Style="width:100%;">
                        @foreach (var ci in _chartInstruments)
                        {
                            <MudSelectItem T="ChartInstrument" Value="@ci">@ci.Label</MudSelectItem>
                        }
                    </MudSelect>
                    @if (_selectedChart is null)
                    {
                        <MudText Typo="Typo.body2">Position anklicken oder Instrument wählen.</MudText>
                    }
                    else if (_candles.Count == 0)
                    {
                        <MudText Typo="Typo.body2">Keine Kursdaten für @_selectedChart.Label.</MudText>
                    }
                    else
                    {
                        <LightweightChart Kind="candlestick" Candles="_candles" Dark="_dark"
                                          InitialRangeDays="_rangeDays" ReferencePrice="_referencePrice" Height="320px" />
                    }
                </ChildContent>
            </SectionCard>
        </MudItem>
```

- [ ] **Step 3: Add the chart state, model, and logic to `@code`**

Add the following members inside the `@code` block (alongside the existing ones):

```csharp
    public sealed record ChartInstrument(string ProviderSymbol, string Isin, string Currency)
    {
        public string Label => $"{ProviderSymbol} — {Isin} ({Currency})";
    }

    private List<ChartInstrument> _chartInstruments = new();
    private ChartInstrument? _selectedChart;
    private List<LightweightChart.Candle> _candles = new();
    private decimal? _referencePrice;
    private int _rangeDays = 365;
    private bool _dark = true;

    private Color RangeColor(int days) => _rangeDays == days ? Color.Primary : Color.Default;

    private async Task SetRange(int days)
    {
        _rangeDays = days;
        await Task.CompletedTask; // re-render re-pushes InitialRangeDays to the chart
    }

    private async Task LoadChartInstrumentsAsync()
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WealthIqDbContext>();
        var rows = await db.InstrumentListings
            .Where(x => x.ProviderSymbol != "")
            .OrderBy(x => x.ProviderSymbol)
            .Select(x => new { x.ProviderSymbol, x.Isin, x.Currency })
            .ToListAsync();
        _chartInstruments = rows.Select(x => new ChartInstrument(x.ProviderSymbol, x.Isin, x.Currency)).ToList();
    }

    private async Task OnHoldingRowClickImpl(DashboardHolding h)
    {
        if (h.ProviderSymbol is null) return;
        var match = _chartInstruments.FirstOrDefault(c => c.ProviderSymbol == h.ProviderSymbol);
        if (match is not null)
        {
            _referencePrice = h.AverageBuyPriceNative; // null for mixed-currency / combined rows
            await OnChartInstrumentChanged(match, keepReference: true);
        }
    }

    private async Task OnChartInstrumentChanged(ChartInstrument? option) => await OnChartInstrumentChanged(option, keepReference: false);

    private async Task OnChartInstrumentChanged(ChartInstrument? option, bool keepReference)
    {
        _selectedChart = option;
        ChartSelection.SelectedPriceSymbol = option?.ProviderSymbol;
        if (!keepReference)
        {
            // Manual dropdown pick: show the reference line only if it matches a held position's avg buy.
            _referencePrice = Current?.Holdings
                .FirstOrDefault(h => h.ProviderSymbol == option?.ProviderSymbol)?.AverageBuyPriceNative;
        }
        _candles = new();
        if (option is null) return;

        await using var scope = ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WealthIqDbContext>();
        var currency = Enum.TryParse<CurrencyCode>(option.Currency, out var c) ? c : CurrencyCode.EUR;
        var rows = await db.HistoricalPrices
            .Where(x => x.ProviderSymbol == option.ProviderSymbol)
            .OrderBy(x => x.Date)
            .ToListAsync();
        var bars = rows.Select(x => new PriceBar(x.Date, x.ProviderSymbol, currency,
            x.Open, x.High, x.Low, x.Close, x.AdjustedClose, x.Volume)).ToList();
        _candles = AdjustedPriceCalculator.ToAdjusted(bars)
            .Select(b => new LightweightChart.Candle(b.Date.ToString("yyyy-MM-dd"),
                decimal.Round(b.Open, 4), decimal.Round(b.High, 4), decimal.Round(b.Low, 4), decimal.Round(b.Close, 4)))
            .ToList();
    }
```

- [ ] **Step 4: Wire the row-click handler and initial selection**

Replace the existing placeholder `OnHoldingRowClick` method:

```csharp
    private void OnHoldingRowClick(TableRowClickEventArgs<DashboardHolding> args) { }
```

with one that delegates to the impl:

```csharp
    private async Task OnHoldingRowClick(TableRowClickEventArgs<DashboardHolding> args)
    {
        if (args.Item is { } holding) await OnHoldingRowClickImpl(holding);
    }
```

Then extend `LoadAsync()` to also load the chart instrument list and pick an initial chart selection. Add, just before the `finally` block in `LoadAsync()`:

```csharp
            await LoadChartInstrumentsAsync();
            // Restore remembered selection, else the largest holding, else the first instrument.
            var remembered = ChartSelection.SelectedPriceSymbol;
            var initial = (remembered is not null ? _chartInstruments.FirstOrDefault(c => c.ProviderSymbol == remembered) : null)
                ?? _chartInstruments.FirstOrDefault(c => c.ProviderSymbol == Current?.Holdings.FirstOrDefault(h => !h.PriceMissing)?.ProviderSymbol)
                ?? _chartInstruments.FirstOrDefault();
            if (initial is not null) await OnChartInstrumentChanged(initial);
```

Finally, load the dark-mode preference on first render. Add this method to the `@code` block:

```csharp
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dark = await ThemePreference.LoadIsDarkAsync();
            StateHasChanged();
        }
    }
```

- [ ] **Step 5: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded.

- [ ] **Step 6: Manual smoke test**

Run: `dotnet run --project src/WealthIQ.Web`
Verify on `/`:
- A candlestick chart renders in the right panel; the initial instrument is a held position.
- Clicking a holdings row updates the chart to that instrument and shows the dashed "Ø Kauf" reference line.
- The dropdown can select an instrument you do NOT hold (no reference line), and the chart updates.
- 1M / 6M / 1J buttons change the visible range.
- Toggling dark/light (drawer toggle) restyles the chart.
- Navigate away (e.g. to Kurschart) and back — the last-selected instrument is restored.

Stop the app.

- [ ] **Step 7: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Dashboard/Dashboard.razor
git commit -m "feat: master-detail price chart on the portfolio dashboard"
```

---

## Task 9: Documentation & full verification

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update CLAUDE.md scope & feature notes**

In `CLAUDE.md`, under "What this project is", remove `portfolio valuation/charts` from the "Explicitly out of v1 scope" list, and add a sentence noting the shipped portfolio dashboard. Then add a bullet to the "Web UI" section describing the new page. Insert this bullet after the existing **Data Browser** bullet:

```markdown
- **Portfolio dashboard** (`Components/Pages/Dashboard/Dashboard.razor`, route `/` — the landing page; Steuerreport moved to `/steuerreport`) shows current holdings grouped by ISIN with average buy price, EUR market value, and unrealized P&L, an asset-class allocation donut, YTD KPIs (Gesamtwert securities-only, unrealized G/V, Dividenden, Realisiert), a Yahoo price-refresh button (`HistoricalPriceRefreshService`), and a master–detail candlestick (`LightweightChart` with an optional `ReferencePrice` avg-buy line; instrument picker spans all priced instruments incl. non-held). Valuation logic lives in `Application/Valuation/PortfolioValuationService` (extended with cost basis / avg buy / unrealized P&L); ISIN rollup + "Alle" view + allocation + YTD dividends/realized live in `Application/Dashboard/PortfolioDashboardService`. Unlike the tax engine, the dashboard is **resilient**: a missing price/FX flags one position ("Kurs fehlt") and excludes it from totals instead of failing. Planned follow-up phases: **Pillar 2** (net worth over time, incl. cash) and **Pillar 3** (rebalancing / target positions).
```

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test WealthIQ.slnx`
Expected: PASS (all existing tests plus the new valuation and dashboard tests).

- [ ] **Step 3: Format check**

Run: `dotnet format WealthIQ.slnx --verify-no-changes`
Expected: no formatting changes required. (If it reports changes, run `dotnet format WealthIQ.slnx` and re-run the verify.)

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document portfolio dashboard in CLAUDE.md"
```

---

## Self-review checklist (completed by plan author)

- **Spec coverage:** holdings grouped by ISIN + avg buy (Task 2), account selector + "Alle" EUR rollup with mixed-currency safety (Task 1 cost-basis-in-EUR + Task 2 rollup), KPIs incl. Realisiert YTD (Task 3), cash excluded / Gesamtwert "Wertpapiere" (Task 7), resilient missing data (Task 1 + Task 2), asset-class donut (Task 7), refresh button (Task 7), master–detail chart with non-held picker + avg-buy line + range + persistence (Task 5 + Task 8), `/` landing + Steuerreport reroute + nav (Task 6), DI (Task 4), CLAUDE.md (Task 9), tests + format (Task 9). All spec sections map to a task.
- **Out of scope honored:** no time-series chart, no cash net worth, no rebalancing, allocation by asset class only.
- **Signatures verified against the codebase:** `Account(AccountId, AccountNumber)`; `PortfolioLedger(entries, instruments?, accounts?)`; `LotMatchingPolicy` in `Domain.Enumeration`; `DataRefreshResult(Added, Updated, Skipped, Diagnostics)` + `HasBlockingDiagnostics`; `Currency` enum members. One remaining implementation-time check: the `LotConsumption` property names used in Task 3 (`CloseUnitPrice`, `OpenUnitPrice`, `MatchedQuantity`, `AllocatedCloseFees`, `AllocatedOpenFees`, `CloseTradeDate`, `OpenTradeDate`, `AccountId`) — these match `FiFoMatcher`'s construction of `LotConsumption`, but confirm against `Domain/Model/Matching/LotConsumption.cs`.
```
