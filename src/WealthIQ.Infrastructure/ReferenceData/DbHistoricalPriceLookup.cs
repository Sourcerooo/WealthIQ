using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Infrastructure.Persistence;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Historical bars from the seeded/refreshed <c>HistoricalPrices</c> table. Loaded once on
/// construction. Selection logic mirrors <see cref="WealthIQ.Infrastructure.Ibkr.MarketData.CsvHistoricalPriceLookup"/>:
/// ExactDate, LatestOnOrBefore, EarliestOnOrAfter; a genuinely missing bar is a blocking error.
/// Rows whose currency text is not a known <c>Currency</c> are ignored.</summary>
public sealed class DbHistoricalPriceLookup : IHistoricalPriceLookup
{
    private readonly Dictionary<string, SortedDictionary<DateOnly, PriceBar>> _barsBySymbol =
        new(StringComparer.OrdinalIgnoreCase);

    public DbHistoricalPriceLookup(WealthIqDbContext db)
    {
        foreach (var row in db.HistoricalPrices)
        {
            if (!Enum.TryParse<CurrencyCode>(row.Currency, ignoreCase: true, out var currency))
            {
                continue;
            }

            if (!_barsBySymbol.TryGetValue(row.ProviderSymbol, out var barsByDate))
            {
                barsByDate = new SortedDictionary<DateOnly, PriceBar>();
                _barsBySymbol[row.ProviderSymbol] = barsByDate;
            }

            barsByDate[row.Date] = new PriceBar(
                row.Date, row.ProviderSymbol, currency,
                row.Open, row.High, row.Low, row.Close, row.AdjustedClose, row.Volume);
        }
    }

    public PriceBar GetPriceBar(
        DateOnly pricingDate,
        string providerSymbol,
        PriceLookupDateHandling dateHandling = PriceLookupDateHandling.LatestOnOrBefore)
    {
        if (!_barsBySymbol.TryGetValue(providerSymbol, out var barsByDate))
        {
            throw new InvalidOperationException($"No historical prices available for provider symbol '{providerSymbol}'.");
        }

        if (dateHandling == PriceLookupDateHandling.ExactDate)
        {
            if (barsByDate.TryGetValue(pricingDate, out var exactBar))
            {
                return exactBar;
            }

            throw new InvalidOperationException($"No historical price available for '{providerSymbol}' on '{pricingDate:yyyy-MM-dd}'.");
        }

        if (dateHandling == PriceLookupDateHandling.EarliestOnOrAfter)
        {
            foreach (var candidate in barsByDate)
            {
                if (candidate.Key >= pricingDate)
                {
                    return candidate.Value;
                }
            }

            throw new InvalidOperationException($"No historical price available for '{providerSymbol}' on or after '{pricingDate:yyyy-MM-dd}'.");
        }

        foreach (var candidate in barsByDate.Reverse())
        {
            if (candidate.Key <= pricingDate)
            {
                return candidate.Value;
            }
        }

        throw new InvalidOperationException($"No historical price available for '{providerSymbol}' on or before '{pricingDate:yyyy-MM-dd}'.");
    }
}
