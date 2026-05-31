namespace WealthIQ.Application.MarketData.Interface;

public interface IHistoricalPriceLookup
{
    PriceBar GetPriceBar(
        DateOnly pricingDate,
        string providerSymbol,
        PriceLookupDateHandling dateHandling = PriceLookupDateHandling.LatestOnOrBefore);
}
