using System.Globalization;
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;

namespace WealthIQ.Infrastructure.IBKR.MarketData;

public sealed class CsvHistoricalPriceLookup : IHistoricalPriceLookup
{
    private readonly Dictionary<string, SortedDictionary<DateOnly, PriceBar>> _barsBySymbol = new(StringComparer.OrdinalIgnoreCase);

    public CsvHistoricalPriceLookup(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Historical price file not found.", filePath);
        }

        foreach (var line in File.ReadLines(filePath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length < 9
                || !DateOnly.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !Enum.TryParse<WealthIQ.Domain.Enumeration.Currency>(parts[2].Trim(), true, out var currency)
                || !decimal.TryParse(parts[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var open)
                || !decimal.TryParse(parts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var high)
                || !decimal.TryParse(parts[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var low)
                || !decimal.TryParse(parts[6].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var close)
                || !decimal.TryParse(parts[7].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var adjustedClose)
                || !long.TryParse(parts[8].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var volume))
            {
                continue;
            }

            var providerSymbol = parts[1].Trim();
            if (!_barsBySymbol.TryGetValue(providerSymbol, out var barsByDate))
            {
                barsByDate = new SortedDictionary<DateOnly, PriceBar>();
                _barsBySymbol[providerSymbol] = barsByDate;
            }

            barsByDate[date] = new PriceBar(date, providerSymbol, currency, open, high, low, close, adjustedClose, volume);
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
