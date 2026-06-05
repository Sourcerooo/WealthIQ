using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Application.Tax.Interface;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Derives the redemption price from stored <c>HistoricalPrice</c> bars: resolves the listing
/// symbol via <see cref="IInstrumentMarketDataMap"/>, reads the bar via <see cref="IHistoricalPriceLookup"/>,
/// and returns (Close, barCurrency, barDate). Asserts the bar currency equals the requested currency
/// (else blocking error — mis-mapped listing). Does NOT do FX; conversion stays in the calculator
/// (spec §5.4). Replaces IYearEndPriceProvider / DbYearEndPriceProvider entirely.</summary>
public sealed class DerivedInstrumentPriceProvider(
    IInstrumentMarketDataMap marketDataMap,
    IHistoricalPriceLookup priceLookup) : IInstrumentPriceProvider
{
    public InstrumentQuote? GetQuote(string isin, CurrencyCode currency, DateOnly pricingDate, PriceQuoteHandling handling)
    {
        var profile = marketDataMap.GetProfile(isin, currency);
        var bar = priceLookup.GetPriceBar(pricingDate, profile.ProviderSymbol, Map(handling));

        if (bar.Currency != currency)
        {
            throw new InvalidOperationException(
                $"Historical bar for '{isin}' ({profile.ProviderSymbol}) is in {bar.Currency} but the held lot is in {currency}. " +
                $"The listing is mis-mapped.");
        }

        return new InstrumentQuote(bar.Close, bar.Currency, bar.Date);
    }

    private static PriceLookupDateHandling Map(PriceQuoteHandling handling) => handling switch
    {
        PriceQuoteHandling.LatestOnOrBefore => PriceLookupDateHandling.LatestOnOrBefore,
        PriceQuoteHandling.EarliestOnOrAfter => PriceLookupDateHandling.EarliestOnOrAfter,
        PriceQuoteHandling.ExactDate => PriceLookupDateHandling.ExactDate,
        _ => throw new ArgumentOutOfRangeException(nameof(handling))
    };
}
