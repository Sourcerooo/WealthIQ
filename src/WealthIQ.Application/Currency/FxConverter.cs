using WealthIQ.Application.Currency.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.Currency;

public sealed class FxConverter(IFxRateLookup fxRateLookup, CurrencyCode baseCurrency = CurrencyCode.EUR)
{
    public CurrencyCode BaseCurrency { get; } = baseCurrency;

    public Money Convert(
        Money amount,
        DateOnly conversionDate,
        FxRateLookupDateHandling dateHandling = FxRateLookupDateHandling.ExactDate)
    {
        var rate = fxRateLookup.GetRate(conversionDate, amount.Currency, BaseCurrency, dateHandling);
        return new Money(amount.Amount * rate, BaseCurrency);
    }
}
