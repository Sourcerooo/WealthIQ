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

    [Fact]
    public void Calculate_SellClosingMultipleLots_WithheldKestSplitsProportionallyAndSumsToTotal()
    {
        var account = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "DE0002", "BBB", "Beta", 0m)
        {
            SubjectToVorabpauschale = false
        };

        // Two buys create two separate FIFO lots: 4 shares then 6 shares, both @ 100 EUR.
        var buy1 = TaxEntries.Trade(account, instrumentId, TradeSide.Buy, 4m, 100m,
            new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero), "BUY-1");
        var buy2 = TaxEntries.Trade(account, instrumentId, TradeSide.Buy, 6m, 100m,
            new DateTimeOffset(2024, 2, 10, 12, 0, 0, TimeSpan.Zero), "BUY-2");

        // Sell all 10 shares @ 120 EUR with 9.00 EUR broker-withheld KESt.
        var sell = new TradeEntry(
            PortfolioEntryId.NewId(), account,
            new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 6, 10), TaxEntries.Provenance("SELL-1"),
            instrumentId, TradeSide.Sell, new Quantity(10m),
            new Money(120m, CurrencyCode.EUR), new Money(0m, CurrencyCode.EUR),
            new Money(0m, CurrencyCode.EUR), new Money(9m, CurrencyCode.EUR));

        var ledger = new PortfolioLedger(new PortfolioEntry[] { buy1, buy2, sell }, new[] { instrument });

        var calculator = new GermanTaxCalculator(
            new FakeBasisInterestRateProvider((2024, 0m)),
            new FakeYearEndPriceProvider(),
            new FakeFxRateLookup());

        var result = calculator.Calculate(ledger, new[] { instrument });

        var sells = result.Entries.Where(e => e.Type == GermanTaxEntryType.Sell).ToList();

        // FIFO must have produced exactly two Sell entries (one per consumed lot).
        Assert.Equal(2, sells.Count);

        // All sell entries must be tagged with the correct account.
        Assert.All(sells, e => Assert.Equal(account, e.AccountId));

        // The sum of the per-lot KESt slices must exactly equal the total withheld KESt.
        Assert.Equal(9.00m, sells.Sum(e => e.WithheldKESt));

        // KESt is allocated proportional to the matched quantity (4/10 and 6/10 of 9.00).
        // Sort by slice size ascending to get a deterministic order for the proportional check.
        var slices = sells.Select(e => e.WithheldKESt).OrderBy(x => x).ToList();
        Assert.Equal(3.60m, slices[0]); // 9.00 * 4/10
        Assert.Equal(5.40m, slices[1]); // 9.00 * 6/10
    }
}
