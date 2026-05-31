using WealthIQ.Application.Currency.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using WealthIQ.Tests.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class DbFxRateLookupTests
{
    private static InMemorySqlite SeededDb()
    {
        var db = new InMemorySqlite();
        using var ctx = db.NewContext();
        ctx.FxRates.AddRange(
            new FxRateRow { Date = new DateOnly(2021, 3, 26), Currency = "USD", RateToEur = 0.8487523341m },
            new FxRateRow { Date = new DateOnly(2021, 3, 29), Currency = "USD", RateToEur = 0.8501000000m },
            new FxRateRow { Date = new DateOnly(2021, 3, 26), Currency = "GBP", RateToEur = 1.1695496064m });
        ctx.SaveChanges();
        return db;
    }

    private static DbFxRateLookup Lookup(InMemorySqlite db) => new(db.NewContext());

    [Fact]
    public void GetRate_SameCurrency_ReturnsOne()
    {
        using var db = SeededDb();
        Assert.Equal(1m, Lookup(db).GetRate(new DateOnly(2099, 1, 1), Currency.EUR, Currency.EUR));
    }

    [Fact]
    public void GetRate_ExactDate_ReturnsConfiguredRate()
    {
        using var db = SeededDb();
        Assert.Equal(0.8487523341m, Lookup(db).GetRate(new DateOnly(2021, 3, 26), Currency.USD, Currency.EUR));
    }

    [Fact]
    public void GetRate_MissingDate_ExactHandling_Throws()
    {
        using var db = SeededDb();
        var lookup = Lookup(db);
        Assert.Throws<InvalidOperationException>(() =>
            lookup.GetRate(new DateOnly(2021, 3, 27), Currency.USD, Currency.EUR, FxRateLookupDateHandling.ExactDate));
    }

    [Fact]
    public void GetRate_MissingDate_NextAvailableOnOrAfter_RollsForwardToNextTradingDay()
    {
        using var db = SeededDb();
        var rate = Lookup(db).GetRate(new DateOnly(2021, 3, 27), Currency.USD, Currency.EUR, FxRateLookupDateHandling.NextAvailableOnOrAfter);
        Assert.Equal(0.8501000000m, rate);
    }

    [Fact]
    public void GetRate_ExactDatePresent_NextAvailableHandling_ReturnsExactNotRolled()
    {
        using var db = SeededDb();
        var rate = Lookup(db).GetRate(new DateOnly(2021, 3, 26), Currency.USD, Currency.EUR, FxRateLookupDateHandling.NextAvailableOnOrAfter);
        Assert.Equal(0.8487523341m, rate);
    }

    [Fact]
    public void GetRate_AfterLastAvailableDate_NextAvailableHandling_Throws()
    {
        using var db = SeededDb();
        var lookup = Lookup(db);
        Assert.Throws<InvalidOperationException>(() =>
            lookup.GetRate(new DateOnly(2021, 4, 1), Currency.USD, Currency.EUR, FxRateLookupDateHandling.NextAvailableOnOrAfter));
    }

    [Fact]
    public void GetRate_TargetCurrencyNotEur_Throws()
    {
        using var db = SeededDb();
        var lookup = Lookup(db);
        Assert.Throws<InvalidOperationException>(() =>
            lookup.GetRate(new DateOnly(2021, 3, 26), Currency.GBP, Currency.USD));
    }
}
