using System.Globalization;
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Domain.Enumeration;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.Ibkr.Currency;

public sealed class CsvFxRateLookup : IFxRateLookup
{
    private readonly Dictionary<(DateOnly Date, CurrencyCode Currency), decimal> _rates = new();
    private readonly Dictionary<CurrencyCode, List<DateOnly>> _datesByCurrency = new();

    public CsvFxRateLookup(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("FX rate file not found.", filePath);
        }

        foreach (var line in File.ReadLines(filePath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length < 3)
            {
                continue;
            }

            if (!DateOnly.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !Enum.TryParse<CurrencyCode>(parts[1].Trim(), true, out var currency)
                || !decimal.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rate)
                || rate <= 0m)
            {
                continue;
            }

            _rates[(date, currency)] = rate;

            if (!_datesByCurrency.TryGetValue(currency, out var dates))
            {
                dates = [];
                _datesByCurrency[currency] = dates;
            }

            dates.Add(date);
        }

        foreach (var currency in _datesByCurrency.Keys.ToList())
        {
            _datesByCurrency[currency] = _datesByCurrency[currency]
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }
    }

    public decimal GetRate(
        DateOnly conversionDate,
        CurrencyCode sourceCurrency,
        CurrencyCode targetCurrency,
        FxRateLookupDateHandling dateHandling = FxRateLookupDateHandling.ExactDate)
    {
        if (sourceCurrency == targetCurrency)
        {
            return 1m;
        }

        if (targetCurrency != CurrencyCode.EUR)
        {
            throw new InvalidOperationException($"Target currency '{targetCurrency}' is not supported by the CSV FX lookup.");
        }

        if (_rates.TryGetValue((conversionDate, sourceCurrency), out var exactRate))
        {
            return exactRate;
        }

        if (dateHandling == FxRateLookupDateHandling.NextAvailableOnOrAfter
            && _datesByCurrency.TryGetValue(sourceCurrency, out var availableDates))
        {
            var nextDate = availableDates.FirstOrDefault(x => x >= conversionDate);
            if (nextDate != default && _rates.TryGetValue((nextDate, sourceCurrency), out var nextRate))
            {
                return nextRate;
            }
        }

        throw new InvalidOperationException(
            $"FX rate missing for {sourceCurrency}->{targetCurrency} on '{conversionDate:yyyy-MM-dd}' with handling '{dateHandling}'.");
    }
}
