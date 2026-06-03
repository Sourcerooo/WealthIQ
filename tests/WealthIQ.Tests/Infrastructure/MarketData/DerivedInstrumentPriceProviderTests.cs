using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Infrastructure.MarketData;

public sealed class DerivedInstrumentPriceProviderTests
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
    public void GetQuote_ResolvesSymbolAndReturnsCloseInListingCurrency()
    {
        using var db = NewDb();
        db.InstrumentListings.Add(new InstrumentListingRow
        { Isin = "IE00B3XXRP09", Currency = "GBP", Provider = "YahooFinance", ProviderSymbol = "VUSA.L" });
        db.HistoricalPrices.Add(new HistoricalPriceRow
        { ProviderSymbol = "VUSA.L", Date = new DateOnly(2024, 12, 30), Currency = "GBP",
          Open = 1, High = 1, Low = 1, Close = 90, AdjustedClose = 90, Volume = 1 });
        db.SaveChanges();

        var provider = new DerivedInstrumentPriceProvider(new DbInstrumentMarketDataMap(db), new DbHistoricalPriceLookup(db));
        var quote = provider.GetQuote("IE00B3XXRP09", CurrencyCode.GBP, new DateOnly(2024, 12, 31), PriceQuoteHandling.LatestOnOrBefore);

        Assert.NotNull(quote);
        Assert.Equal(90m, quote!.Value.Close);
        Assert.Equal(CurrencyCode.GBP, quote.Value.Currency);
        Assert.Equal(new DateOnly(2024, 12, 30), quote.Value.AsOf);
    }

    [Fact]
    public void GetQuote_BarCurrencyMismatch_Throws()
    {
        using var db = NewDb();
        db.InstrumentListings.Add(new InstrumentListingRow
        { Isin = "IE00B3XXRP09", Currency = "GBP", Provider = "YahooFinance", ProviderSymbol = "VUSA.L" });
        db.HistoricalPrices.Add(new HistoricalPriceRow
        { ProviderSymbol = "VUSA.L", Date = new DateOnly(2024, 12, 30), Currency = "USD",
          Open = 1, High = 1, Low = 1, Close = 90, AdjustedClose = 90, Volume = 1 });
        db.SaveChanges();

        var provider = new DerivedInstrumentPriceProvider(new DbInstrumentMarketDataMap(db), new DbHistoricalPriceLookup(db));
        Assert.Throws<InvalidOperationException>(() =>
            provider.GetQuote("IE00B3XXRP09", CurrencyCode.GBP, new DateOnly(2024, 12, 31), PriceQuoteHandling.LatestOnOrBefore));
    }
}
