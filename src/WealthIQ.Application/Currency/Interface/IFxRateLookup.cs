using WealthIQ.Domain.Enumeration;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.Currency.Interface;

public interface IFxRateLookup
{
    decimal GetRate(
        DateOnly conversionDate,
        CurrencyCode sourceCurrency,
        CurrencyCode targetCurrency,
        FxRateLookupDateHandling dateHandling = FxRateLookupDateHandling.ExactDate);
}
