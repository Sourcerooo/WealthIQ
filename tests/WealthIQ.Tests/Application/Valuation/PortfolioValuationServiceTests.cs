using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Application.Valuation;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Application.Valuation;

public sealed class PortfolioValuationServiceTests
{
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
        Assert.Equal(90m, position.CostBasisInBaseCurrency);
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
        Assert.Equal(420m, position.CostBasisInBaseCurrency);
        Assert.Equal(105m, position.AverageBuyPriceInBaseCurrency);
        Assert.Equal(480m, position.MarketValueInBaseCurrency);
        Assert.Equal(60m, position.UnrealizedPnlInBaseCurrency);
    }

    private static SourceProvenance CreateSourceProvenance(string sourceReference)
        => new()
        {
            SourceSystem = "TEST",
            ImportFormat = "TEST",
            SourceLocation = "unit-test",
            SourceRecordReference = sourceReference
        };

    private sealed class StubHistoricalPriceLookup(PriceBar bar) : IHistoricalPriceLookup
    {
        public PriceBar GetPriceBar(DateOnly pricingDate, string providerSymbol, PriceLookupDateHandling dateHandling = PriceLookupDateHandling.LatestOnOrBefore)
            => bar;
    }

    private sealed class StubInstrumentMarketDataMap(InstrumentMarketDataProfile profile) : IInstrumentMarketDataMap
    {
        public InstrumentMarketDataProfile GetProfile(string isin, CurrencyCode currency) => profile;
    }

    private sealed class MissingInstrumentMarketDataMap : IInstrumentMarketDataMap
    {
        public InstrumentMarketDataProfile GetProfile(string isin, CurrencyCode currency)
            => throw new InvalidOperationException("Missing market-data mapping.");
    }

    private sealed class StubFxRateLookup : IFxRateLookup
    {
        public decimal GetRate(DateOnly conversionDate, CurrencyCode sourceCurrency, CurrencyCode targetCurrency, FxRateLookupDateHandling dateHandling = FxRateLookupDateHandling.ExactDate)
            => sourceCurrency == targetCurrency ? 1m : throw new InvalidOperationException("Unexpected FX conversion in valuation test.");
    }
}
