using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Domain.Model.Tax;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Application.Tax;

public sealed class GermanTaxCalculatorAssetClassTests
{
    [Fact]
    public void Calculate_ClassifiedFund_CopiesAssetClassAndNameOntoEveryEntry()
    {
        var instrumentId = InstrumentId.NewId();
        var instruments = new[]
        {
            new Instrument(instrumentId, "IE00B6R52259", "ACWI", "Test Equity Fund", 0.30m)
            {
                SubjectToVorabpauschale = true,
                AssetClass = TaxAssetClass.EquityFund
            }
        };

        var result = Run(instruments, instrumentId);

        // Dividend, Vorabpauschale and Sell — every entry the fund produces.
        Assert.Equal(3, result.Entries.Count);
        Assert.All(result.Entries, entry =>
        {
            Assert.Equal(TaxAssetClass.EquityFund, entry.AssetClass);
            Assert.Equal("Test Equity Fund", entry.InstrumentName);
        });
    }

    [Fact]
    public void Calculate_UnclassifiedInstrument_LeavesAssetClassNull()
    {
        var instrumentId = InstrumentId.NewId();
        var instruments = new[]
        {
            new Instrument(instrumentId, "IE00B6R52259", "ACWI", "Unclassified Fund", 0.30m)
            {
                SubjectToVorabpauschale = true
            }
        };

        var result = Run(instruments, instrumentId);

        // The calculator must not invent a classification.
        Assert.All(result.Entries, entry => Assert.Null(entry.AssetClass));
    }

    private static GermanTaxCalculationResult Run(IReadOnlyList<Instrument> instruments, InstrumentId instrumentId)
    {
        var accountId = AccountId.NewId();

        var calculator = new GermanTaxCalculator(
            new StubInterestRateProvider((2024, 0.025m)),
            new StubYearStartAndEndPriceProvider(("IE00B6R52259", 2024, 100m, 120m)),
            new StubFxRateLookup());

        return calculator.Calculate(new PortfolioLedger([
            new TradeEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero),
                new DateOnly(2024, 1, 15),
                TaxCalculatorTestDoubles.SourceProvenance("BUY-1"),
                instrumentId,
                TradeSide.Buy,
                new Quantity(10m),
                new Money(100m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR)),
            new CashEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero),
                new DateOnly(2024, 6, 10),
                TaxCalculatorTestDoubles.SourceProvenance("DIV-1"),
                InstrumentId.NewId(),
                CashFlowType.Dividend,
                new Money(5m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                instrumentId),
            new TradeEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2025, 2, 1, 9, 0, 0, TimeSpan.Zero),
                new DateOnly(2025, 2, 1),
                TaxCalculatorTestDoubles.SourceProvenance("SELL-1"),
                instrumentId,
                TradeSide.Sell,
                new Quantity(10m),
                new Money(130m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR))
        ]), instruments);
    }

}
