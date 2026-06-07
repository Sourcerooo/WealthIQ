using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Dashboard;
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Application.Persistence;
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
                Buy(accountB, instrumentId, "BUY-B", new DateOnly(2025, 5, 1), 3m, 120m),
            ],
            [instrument],
            [new Account(accountA, "ACC-A"), new Account(accountB, "ACC-B")]);

        var service = BuildService(ledger, closePrice: 120m);

        var report = await service.GenerateAsync(Today);

        Assert.Equal("ALL", report.Views[0].AccountKey);
        var all = report.Views[0];
        var holding = Assert.Single(all.Holdings);
        Assert.Equal(5m, holding.Quantity);
        Assert.Equal(560m, holding.CostBasisInBaseCurrency);
        Assert.Equal(112m, holding.AverageBuyPriceInBaseCurrency);
        Assert.Equal(600m, holding.MarketValueInBaseCurrency);
        Assert.Equal(40m, holding.UnrealizedPnlInBaseCurrency);
        Assert.Equal(600m, all.Kpis.TotalSecuritiesValueInBaseCurrency);
        Assert.Equal(2, all.Kpis.AccountCount);

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
                Dividend(account, instrumentId, "DIV-OLD", new DateOnly(2024, 3, 1), 999m),
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

        Assert.Equal(120m, report.Views[0].Kpis.RealizedYtdInBaseCurrency);
    }

    // ---- helpers / stubs (shared with Task 3) ----

    internal static TradeEntry Buy(AccountId account, InstrumentId instrument, string reference, DateOnly date, decimal qty, decimal price)
        => new(
            PortfolioEntryId.NewId(), account,
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), date,
            Provenance(reference), instrument, TradeSide.Buy,
            new Quantity(qty), new Money(price, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR));

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
            Provenance(reference), instrument, CashFlowType.Dividend,
            new Money(amount, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR));

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
        public Instrument Enrich(Instrument instrument) => instrument;
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
