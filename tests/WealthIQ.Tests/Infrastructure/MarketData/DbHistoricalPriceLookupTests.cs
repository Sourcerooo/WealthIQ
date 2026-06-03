using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Infrastructure.MarketData;

public sealed class DbHistoricalPriceLookupTests
{
    private static WealthIqDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new WealthIqDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public void GetPriceBar_LatestOnOrBefore_ReadsClosingBarInListingCurrency()
    {
        using var db = NewDb();
        db.HistoricalPrices.Add(new HistoricalPriceRow
        {
            ProviderSymbol = "VUSA.L", Date = new DateOnly(2024, 12, 30), Currency = "GBP",
            Open = 1, High = 1, Low = 1, Close = 130, AdjustedClose = 130, Volume = 10
        });
        db.SaveChanges();

        var lookup = new DbHistoricalPriceLookup(db);
        var bar = lookup.GetPriceBar(new DateOnly(2024, 12, 31), "VUSA.L", PriceLookupDateHandling.LatestOnOrBefore);

        Assert.Equal(130m, bar.Close);
        Assert.Equal(WealthIQ.Domain.Enumeration.Currency.GBP, bar.Currency);
    }
}
