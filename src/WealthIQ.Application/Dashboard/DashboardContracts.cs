using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.Dashboard;

/// <summary>One holding row, grouped by ISIN (within an account, or across all accounts for the "Alle" view).
/// Market value / P&L are null when any underlying position lacks a usable price (resilient display).</summary>
public sealed record DashboardHolding(
    string? Isin,
    string Symbol,
    string Name,
    string AssetClass,
    decimal Quantity,
    decimal AverageBuyPriceInBaseCurrency,
    decimal? AverageBuyPriceNative,
    CurrencyCode? NativeCurrency,
    decimal? ClosePrice,
    CurrencyCode? PriceCurrency,
    decimal CostBasisInBaseCurrency,
    decimal? MarketValueInBaseCurrency,
    decimal? UnrealizedPnlInBaseCurrency,
    /// <summary>Unrealized P&amp;L as a RATIO of cost basis (0.0714 = 7.14 %), not a pre-multiplied percentage.</summary>
    decimal? UnrealizedPnlPct,
    string? ProviderSymbol,
    bool PriceMissing);

public sealed record DashboardAllocationSlice(
    string AssetClass,
    decimal ValueInBaseCurrency,
    /// <summary>Share of the priced total as a PERCENTAGE already multiplied by 100 (e.g. 52.0 = 52 %).</summary>
    decimal Percent);

public sealed record DashboardKpis(
    decimal TotalSecuritiesValueInBaseCurrency,
    decimal UnrealizedPnlInBaseCurrency,
    /// <summary>Unrealized P&amp;L as a RATIO of cost basis (0.0714 = 7.14 %), not a pre-multiplied percentage.</summary>
    decimal UnrealizedPnlPct,
    decimal DividendsYtdInBaseCurrency,
    decimal RealizedYtdInBaseCurrency,
    int PositionCount,
    int AccountCount,
    int PriceMissingCount);

/// <summary>One selectable view: either a single account or the combined "Alle" view.</summary>
public sealed record DashboardView(
    string AccountKey,            // "ALL" or the account id (Guid string)
    string AccountLabel,          // "Alle Konten" or the account number
    IReadOnlyList<DashboardHolding> Holdings,
    IReadOnlyList<DashboardAllocationSlice> Allocation,
    DashboardKpis Kpis);

public sealed record PortfolioDashboardReport(
    DateOnly ValuationDate,
    DateOnly EffectivePriceDate,
    IReadOnlyList<DashboardView> Views);
