using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.MarketData;

public sealed record HistoricalPriceFetchResult(
    string ProviderSymbol,
    CurrencyCode Currency,
    IReadOnlyList<PriceBar> Bars);
