using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Application.Valuation;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Tests.Application.Valuation;

public sealed class PortfolioValuationServiceTests
{
    [Fact]
    public void Calculate_LongPositionAndCash_UsesLatestCloseOnOrBeforeDate()
    {
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "IE00TEST1234", "TEST", "Test ETF", 0.30m);

        var service = new PortfolioValuationService(
            new StubHistoricalPriceLookup(new PriceBar(new DateOnly(2025, 5, 2), "TEST.AS", Currency.EUR, 100m, 102m, 99m, 101m, 101m, 1000)),
            new StubInstrumentMarketDataMap(new InstrumentMarketDataProfile("YahooFinance", "TEST.AS")),
            new StubFxRateLookup());

        var ledger = new PortfolioLedger([
            new TradeEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2025, 5, 1, 10, 0, 0, TimeSpan.Zero),
                new DateOnly(2025, 5, 1),
                CreateSourceProvenance("BUY-1"),
                instrumentId,
                TradeSide.Buy,
                new Quantity(2m),
                new Money(90m, Currency.EUR),
                new Money(1m, Currency.EUR),
                new Money(0m, Currency.EUR)),
            new CashEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2025, 5, 1, 20, 0, 0, TimeSpan.Zero),
                new DateOnly(2025, 5, 1),
                CreateSourceProvenance("INT-1"),
                InstrumentId.NewId(),
                CashFlowType.Interest,
                new Money(10m, Currency.EUR),
                new Money(0m, Currency.EUR),
                new Money(0m, Currency.EUR))
        ], [instrument]);

        var snapshot = service.Calculate(ledger, [instrument], new DateOnly(2025, 5, 4));

        Assert.Equal(new DateOnly(2025, 5, 2), snapshot.EffectiveMarketDate);
        Assert.Single(snapshot.Positions);
        Assert.Equal(202m, snapshot.Positions[0].MarketValueInBaseCurrency);
        Assert.Single(snapshot.CashBalances);
        Assert.Equal(-171m, snapshot.CashBalances[0].AmountInBaseCurrency);
        Assert.Equal(31m, snapshot.TotalValueInBaseCurrency);
    }

    [Fact]
    public void Calculate_MissingMarketDataMapping_ThrowsInvalidOperationException()
    {
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "IE00TEST9999", "TEST", "Test ETF", 0.30m);

        var service = new PortfolioValuationService(
            new StubHistoricalPriceLookup(new PriceBar(new DateOnly(2025, 5, 2), "TEST.AS", Currency.EUR, 100m, 102m, 99m, 101m, 101m, 1000)),
            new MissingInstrumentMarketDataMap(),
            new StubFxRateLookup());

        var ledger = new PortfolioLedger([
            new TradeEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2025, 5, 1, 10, 0, 0, TimeSpan.Zero),
                new DateOnly(2025, 5, 1),
                CreateSourceProvenance("BUY-1"),
                instrumentId,
                TradeSide.Buy,
                new Quantity(1m),
                new Money(90m, Currency.EUR),
                new Money(0m, Currency.EUR),
                new Money(0m, Currency.EUR))
        ], [instrument]);

        Assert.Throws<InvalidOperationException>(() => service.Calculate(ledger, [instrument], new DateOnly(2025, 5, 4)));
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
        public InstrumentMarketDataProfile GetProfile(Instrument instrument) => profile;
    }

    private sealed class MissingInstrumentMarketDataMap : IInstrumentMarketDataMap
    {
        public InstrumentMarketDataProfile GetProfile(Instrument instrument)
            => throw new InvalidOperationException("Missing market-data mapping.");
    }

    private sealed class StubFxRateLookup : IFxRateLookup
    {
        public decimal GetRate(DateOnly conversionDate, Currency sourceCurrency, Currency targetCurrency, FxRateLookupDateHandling dateHandling = FxRateLookupDateHandling.ExactDate)
            => sourceCurrency == targetCurrency ? 1m : throw new InvalidOperationException("Unexpected FX conversion in valuation test.");
    }
}
