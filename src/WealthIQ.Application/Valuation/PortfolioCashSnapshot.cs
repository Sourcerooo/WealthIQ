using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Application.Valuation;

public sealed record PortfolioCashSnapshot(
    string Currency,
    decimal Amount,
    decimal AmountInBaseCurrency);
