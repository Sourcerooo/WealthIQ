using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Event;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Tests.Application.Tax;

public sealed class GermanTaxCalculatorTests
{
    [Fact]
    public void Calculate_BuyDividendVorabAndSell_ProducesExpectedTaxEntries()
    {
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instruments = new[]
        {
            new Instrument(instrumentId, "IE00B6R52259", "ACWI", "ACWI", 0.30m)
        };

        var calculator = new GermanTaxCalculator(
            new StubInterestRateProvider((2024, 0.025m)),
            new StubYearEndPriceProvider(("IE00B6R52259", 2024, 120m)));

        var result = calculator.Calculate([
            new ExecutedTradeEvent(
                AccountEventId.NewId(),
                accountId,
                new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero),
                "IBKR",
                "BUY-1",
                instrumentId,
                TradeSide.Buy,
                new Quantity(10m),
                new Money(100m, Currency.EUR),
                new Money(0m, Currency.EUR),
                new Money(0m, Currency.EUR)),
            new CashIncomeEvent(
                AccountEventId.NewId(),
                accountId,
                new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero),
                EventType.Dividend,
                "IBKR",
                "DIV-1",
                instrumentId,
                CashIncomeType.Dividend,
                new Money(5m, Currency.EUR),
                new Money(0m, Currency.EUR),
                new Money(0m, Currency.EUR)),
            new ExecutedTradeEvent(
                AccountEventId.NewId(),
                accountId,
                new DateTimeOffset(2025, 2, 1, 9, 0, 0, TimeSpan.Zero),
                "IBKR",
                "SELL-1",
                instrumentId,
                TradeSide.Sell,
                new Quantity(10m),
                new Money(130m, Currency.EUR),
                new Money(0m, Currency.EUR),
                new Money(0m, Currency.EUR))
        ], instruments);

        Assert.Equal(3, result.Entries.Count);

        var dividendEntry = result.Entries.Single(x => x.Type == GermanTaxEntryType.Dividend);
        Assert.Equal(5m, dividendEntry.RawAmount);
        Assert.Equal(3.5m, dividendEntry.TaxableAmount);

        var vorabEntry = result.Entries.Single(x => x.Type == GermanTaxEntryType.Vorabpauschale);
        Assert.Equal(new DateOnly(2025, 1, 1), vorabEntry.Date);
        Assert.Equal(12.5m, vorabEntry.RawAmount);
        Assert.Equal(8.75m, vorabEntry.TaxableAmount);

        var sellEntry = result.Entries.Single(x => x.Type == GermanTaxEntryType.Sell);
        Assert.Equal(287.5m, sellEntry.RawAmount);
        Assert.Equal(201.25m, sellEntry.TaxableAmount);
        Assert.Equal(12.5m, sellEntry.UsedVorabpauschale);
        Assert.Equal(10m, sellEntry.QuantitySold);

        Assert.All(result.OpenLots, lot => Assert.Equal(0m, lot.RemainingQuantity.Value));
    }

    [Fact]
    public void Calculate_InterestAndWithholdingTax_ProducesSeparateEntries()
    {
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instruments = new[]
        {
            new Instrument(instrumentId, "", "EUR", "Euro cash", 0m)
        };

        var calculator = new GermanTaxCalculator(
            new StubInterestRateProvider(),
            new StubYearEndPriceProvider());

        var result = calculator.Calculate([
            new CashIncomeEvent(
                AccountEventId.NewId(),
                accountId,
                new DateTimeOffset(2025, 3, 10, 12, 0, 0, TimeSpan.Zero),
                EventType.Interest,
                "IBKR",
                "INT-1",
                instrumentId,
                CashIncomeType.Interest,
                new Money(17.42m, Currency.EUR),
                new Money(0m, Currency.EUR),
                new Money(0m, Currency.EUR)),
            new WithholdingTaxEvent(
                AccountEventId.NewId(),
                accountId,
                new DateTimeOffset(2025, 3, 11, 12, 0, 0, TimeSpan.Zero),
                "IBKR",
                "WHT-1",
                instrumentId,
                new Money(-3.11m, Currency.EUR))
        ], instruments);

        var interestEntry = result.Entries.Single(x => x.Type == GermanTaxEntryType.Interest);
        Assert.Equal(17.42m, interestEntry.RawAmount);
        Assert.Equal(17.42m, interestEntry.TaxableAmount);

        var withholdingTaxEntry = result.Entries.Single(x => x.Type == GermanTaxEntryType.WithholdingTax);
        Assert.Equal(-3.11m, withholdingTaxEntry.RawAmount);
        Assert.Equal(3.11m, withholdingTaxEntry.ForeignWithholdingTax);
    }

    private sealed class StubInterestRateProvider(params (int Year, decimal Rate)[] rates) : IBasisInterestRateProvider
    {
        private readonly Dictionary<int, decimal> _rates = rates.ToDictionary(x => x.Year, x => x.Rate);

        public decimal GetRate(int year) => _rates.GetValueOrDefault(year);
    }

    private sealed class StubYearEndPriceProvider(params (string Isin, int Year, decimal Price)[] prices) : IYearEndPriceProvider
    {
        private readonly Dictionary<(string Isin, int Year), decimal> _prices = prices.ToDictionary(x => (x.Isin, x.Year), x => x.Price);

        public decimal? GetPrice(string isin, int year) => _prices.TryGetValue((isin, year), out var price) ? price : null;
    }
}
