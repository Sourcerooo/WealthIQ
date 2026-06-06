using WealthIQ.Application.Tax;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using Xunit;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

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
            new Money(120m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR),
            new Money(0m, CurrencyCode.EUR), new Money(5m, CurrencyCode.EUR));

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
