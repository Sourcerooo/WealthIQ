using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.Valuation;

public sealed record PortfolioPositionSnapshot(
    AccountId AccountId,
    InstrumentId InstrumentId,
    string Symbol,
    string? Isin,
    PositionDirection Direction,
    decimal Quantity,
    decimal ClosePrice,
    CurrencyCode PriceCurrency,
    decimal MarketValueInBaseCurrency);
