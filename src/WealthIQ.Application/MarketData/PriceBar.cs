using WealthIQ.Domain.Enumeration;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.MarketData;

public sealed record PriceBar(
    DateOnly Date,
    string ProviderSymbol,
    CurrencyCode Currency,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal AdjustedClose,
    long Volume);
