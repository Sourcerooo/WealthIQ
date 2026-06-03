namespace WealthIQ.Application.MarketData;

/// <summary>Politeness/retry knobs for the Yahoo provider, bound from appsettings (spec §2, §5.1).</summary>
public sealed class HistoricalPriceProviderOptions
{
    public string BaseUrl { get; set; } = "https://query1.finance.yahoo.com/v8/finance/chart/";
    public string UserAgent { get; set; } = "Mozilla/5.0";
    public int InterRequestDelayMs { get; set; } = 500;
    public int MaxRetries { get; set; } = 4;
    public int InitialBackoffMs { get; set; } = 1000;
}
