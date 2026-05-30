using WealthIQ.Application.Currency.Interface;
using WealthIQ.Infrastructure.Persistence;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>
/// FX rates from the seeded <c>FxRates</c> table. Loaded once on construction. Reproduces
/// <see cref="WealthIQ.Infrastructure.Ibkr.Currency.CsvFxRateLookup"/>: same currency → 1; target ≠ EUR
/// throws; exact-date hit wins; <see cref="FxRateLookupDateHandling.NextAvailableOnOrAfter"/> rolls forward
/// to the first stored date on or after the requested one; otherwise a missing rate is a blocking error (spec §7).
/// Rows whose currency text is not a known <see cref="CurrencyCode"/> or whose rate is ≤ 0 are ignored.
/// </summary>
public sealed class DbFxRateLookup : IFxRateLookup
{
    private readonly Dictionary<(DateOnly Date, CurrencyCode Currency), decimal> _rates = new();
    private readonly Dictionary<CurrencyCode, List<DateOnly>> _datesByCurrency = new();

    public DbFxRateLookup(WealthIqDbContext db)
    {
        foreach (var row in db.FxRates)
        {
            if (!Enum.TryParse<CurrencyCode>(row.Currency, ignoreCase: true, out var currency) || row.RateToEur <= 0m)
            {
                continue;
            }

            _rates[(row.Date, currency)] = row.RateToEur;

            if (!_datesByCurrency.TryGetValue(currency, out var dates))
            {
                dates = [];
                _datesByCurrency[currency] = dates;
            }

            dates.Add(row.Date);
        }

        foreach (var currency in _datesByCurrency.Keys.ToList())
        {
            _datesByCurrency[currency] = _datesByCurrency[currency].Distinct().OrderBy(x => x).ToList();
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
            throw new InvalidOperationException($"Target currency '{targetCurrency}' is not supported by the DB FX lookup.");
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
