namespace WealthIQ.Application.MarketData;

public sealed record InstrumentMarketDataProfile(
    string Provider,
    string ProviderSymbol,
    string? Notes = null);
