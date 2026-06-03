using Microsoft.EntityFrameworkCore;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Infrastructure.MarketData;

public sealed class DbInstrumentMarketDataMapTests
{
    private static WealthIqDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>().UseSqlite("Data Source=:memory:").Options;
        var db = new WealthIqDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public void GetProfile_ResolvesByIsinAndCurrency()
    {
        using var db = NewDb();
        db.InstrumentListings.Add(new InstrumentListingRow
        { Isin = "IE00B3XXRP09", Currency = "GBP", Provider = "YahooFinance", ProviderSymbol = "VUSA.L" });
        db.SaveChanges();

        var map = new DbInstrumentMarketDataMap(db);
        Assert.Equal("VUSA.L", map.GetProfile("IE00B3XXRP09", CurrencyCode.GBP).ProviderSymbol);
    }

    [Fact]
    public void GetProfile_MissingListing_Throws()
    {
        using var db = NewDb();
        var map = new DbInstrumentMarketDataMap(db);
        Assert.Throws<InvalidOperationException>(() => map.GetProfile("IE00B3XXRP09", CurrencyCode.EUR));
    }
}
