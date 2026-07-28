using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Application.Tax;

internal sealed class StubInterestRateProvider(params (int Year, decimal Rate)[] rates) : IBasisInterestRateProvider
{
    private readonly Dictionary<int, decimal> _rates = rates.ToDictionary(x => x.Year, x => x.Rate);

    public decimal? GetRate(int year) => _rates.TryGetValue(year, out var rate) ? rate : null;
}

internal sealed class StubYearEndPriceProvider(params (string Isin, int Year, decimal Price)[] prices) : IInstrumentPriceProvider
{
    private readonly Dictionary<(string Isin, int Year), decimal> _prices = prices.ToDictionary(x => (x.Isin, x.Year), x => x.Price);

    public InstrumentQuote? GetQuote(string isin, CurrencyCode currency, DateOnly pricingDate, PriceQuoteHandling handling)
        => _prices.TryGetValue((isin, pricingDate.Year), out var price)
            ? new InstrumentQuote(price, CurrencyCode.EUR, pricingDate)
            : null;
}

/// <summary>Returns a distinct start price (EarliestOnOrAfter) and end price (LatestOnOrBefore) per ISIN+year.</summary>
internal sealed class StubYearStartAndEndPriceProvider(params (string Isin, int Year, decimal Start, decimal End)[] prices) : IInstrumentPriceProvider
{
    public InstrumentQuote? GetQuote(string isin, CurrencyCode currency, DateOnly pricingDate, PriceQuoteHandling handling)
    {
        var entry = prices.FirstOrDefault(p => p.Isin == isin && p.Year == pricingDate.Year);
        if (entry == default) return null;
        var price = handling == PriceQuoteHandling.EarliestOnOrAfter ? entry.Start : entry.End;
        return new InstrumentQuote(price, CurrencyCode.EUR, pricingDate);
    }
}

internal sealed class StubFxRateLookup : IFxRateLookup
{
    public decimal GetRate(DateOnly conversionDate, CurrencyCode sourceCurrency, CurrencyCode targetCurrency, FxRateLookupDateHandling dateHandling = FxRateLookupDateHandling.ExactDate)
        => sourceCurrency == targetCurrency && targetCurrency == CurrencyCode.EUR ? 1m : throw new InvalidOperationException("Unexpected FX lookup in unit test.");
}

internal static class TaxCalculatorTestDoubles
{
    internal static SourceProvenance SourceProvenance(string sourceReference)
        => new()
        {
            SourceSystem = "IBKR",
            ImportFormat = "TEST",
            SourceLocation = "unit-test",
            SourceRecordReference = sourceReference
        };
}
