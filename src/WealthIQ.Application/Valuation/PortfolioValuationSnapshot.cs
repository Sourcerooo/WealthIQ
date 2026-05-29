namespace WealthIQ.Application.Valuation;

public sealed record PortfolioValuationSnapshot(
    DateOnly RequestedDate,
    DateOnly EffectiveMarketDate,
    IReadOnlyList<PortfolioPositionSnapshot> Positions,
    IReadOnlyList<PortfolioCashSnapshot> CashBalances,
    decimal TotalValueInBaseCurrency);
