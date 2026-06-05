using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Infrastructure.Ibkr.MarketData;

namespace WealthIQ.Tests.Infrastructure.MarketData;

public sealed class CsvHistoricalPriceLookupTests
{
    private static string WriteCsv()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path,
            "date,provider_symbol,currency,open,high,low,close,adjusted_close,volume\n" +
            "2024-01-02,VUSA.L,GBP,1,1,1,100,100,10\n" +
            "2024-12-30,VUSA.L,GBP,1,1,1,130,130,10\n");
        return path;
    }

    [Fact]
    public void GetPriceBar_EarliestOnOrAfter_ReturnsFirstBarOnOrAfterDate()
    {
        var lookup = new CsvHistoricalPriceLookup(WriteCsv());
        var bar = lookup.GetPriceBar(new DateOnly(2024, 1, 1), "VUSA.L", PriceLookupDateHandling.EarliestOnOrAfter);
        Assert.Equal(new DateOnly(2024, 1, 2), bar.Date);
        Assert.Equal(100m, bar.Close);
    }

    [Fact]
    public void GetPriceBar_LatestOnOrBefore_ReturnsLastBarOnOrBeforeDate()
    {
        var lookup = new CsvHistoricalPriceLookup(WriteCsv());
        var bar = lookup.GetPriceBar(new DateOnly(2024, 12, 31), "VUSA.L", PriceLookupDateHandling.LatestOnOrBefore);
        Assert.Equal(new DateOnly(2024, 12, 30), bar.Date);
        Assert.Equal(130m, bar.Close);
    }
}
