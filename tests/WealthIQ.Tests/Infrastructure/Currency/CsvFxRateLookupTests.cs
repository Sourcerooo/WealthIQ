using WealthIQ.Application.Currency.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Infrastructure.Ibkr.Currency;
using Xunit;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Infrastructure.CurrencyTests;

public sealed class CsvFxRateLookupTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wealthiq-fx-" + Guid.NewGuid().ToString("N"));

    private string WriteCsv(string content)
    {
        Directory.CreateDirectory(_temp);
        var path = Path.Combine(_temp, "fx_rates.csv");
        File.WriteAllText(path, content);
        return path;
    }

    private CsvFxRateLookup Lookup() => new(WriteCsv(
        """
        date,currency,rate_to_eur
        2021-03-26,USD,0.8487523341
        2021-03-29,USD,0.8501000000
        2021-03-26,GBP,1.1695496064
        2021-01-04,JPY,-0.5
        2021-01-05,JPY,not-a-number
        2021-01-06,JPY
        """));

    [Fact]
    public void GetRate_SameCurrency_ReturnsOne()
    {
        var rate = Lookup().GetRate(new DateOnly(2099, 1, 1), CurrencyCode.EUR, CurrencyCode.EUR);
        Assert.Equal(1m, rate);
    }

    [Fact]
    public void GetRate_ExactDate_ReturnsConfiguredRate()
    {
        var rate = Lookup().GetRate(new DateOnly(2021, 3, 26), CurrencyCode.USD, CurrencyCode.EUR);
        Assert.Equal(0.8487523341m, rate);
    }

    [Fact]
    public void GetRate_MissingDate_ExactHandling_Throws()
    {
        // 2021-03-27 is a Saturday with no published rate; ExactDate must not silently substitute.
        var lookup = Lookup();
        Assert.Throws<InvalidOperationException>(() =>
            lookup.GetRate(new DateOnly(2021, 3, 27), CurrencyCode.USD, CurrencyCode.EUR, FxRateLookupDateHandling.ExactDate));
    }

    [Fact]
    public void GetRate_MissingDate_NextAvailableOnOrAfter_RollsForwardToNextTradingDay()
    {
        // 2021-03-27 (Sat) and 2021-03-28 (Sun) have no rate → roll forward to Monday 2021-03-29.
        var rate = Lookup().GetRate(new DateOnly(2021, 3, 27), CurrencyCode.USD, CurrencyCode.EUR, FxRateLookupDateHandling.NextAvailableOnOrAfter);
        Assert.Equal(0.8501000000m, rate);
    }

    [Fact]
    public void GetRate_ExactDatePresent_NextAvailableHandling_ReturnsExactNotRolled()
    {
        var rate = Lookup().GetRate(new DateOnly(2021, 3, 26), CurrencyCode.USD, CurrencyCode.EUR, FxRateLookupDateHandling.NextAvailableOnOrAfter);
        Assert.Equal(0.8487523341m, rate);
    }

    [Fact]
    public void GetRate_AfterLastAvailableDate_NextAvailableHandling_Throws()
    {
        var lookup = Lookup();
        Assert.Throws<InvalidOperationException>(() =>
            lookup.GetRate(new DateOnly(2021, 4, 1), CurrencyCode.USD, CurrencyCode.EUR, FxRateLookupDateHandling.NextAvailableOnOrAfter));
    }

    [Fact]
    public void GetRate_TargetCurrencyNotEur_Throws()
    {
        var lookup = Lookup();
        Assert.Throws<InvalidOperationException>(() =>
            lookup.GetRate(new DateOnly(2021, 3, 26), CurrencyCode.GBP, CurrencyCode.USD));
    }

    [Fact]
    public void GetRate_NegativeNonNumericOrTruncatedRows_AreSkipped()
    {
        // All three JPY rows are invalid (negative, non-numeric, missing column) → no JPY rate exists.
        var lookup = Lookup();
        Assert.Throws<InvalidOperationException>(() =>
            lookup.GetRate(new DateOnly(2021, 1, 4), CurrencyCode.JPY, CurrencyCode.EUR, FxRateLookupDateHandling.NextAvailableOnOrAfter));
    }

    [Fact]
    public void Constructor_FileNotFound_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => new CsvFxRateLookup(Path.Combine(_temp, "missing.csv")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}
