namespace WealthIQ.Application.MarketData.Interface;

public interface IHistoricalPriceProvider
{
    /// <summary>Fetches daily bars for one provider symbol in [from, to], plus the reported listing currency.</summary>
    Task<HistoricalPriceFetchResult> FetchAsync(string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct);
}
