namespace WealthIQ.Application.Currency;

public sealed class FxRateProviderOptions
{
    public string HistoricalUrl { get; set; } = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-hist.xml";
    public string UserAgent { get; set; } = "Mozilla/5.0";
    public IReadOnlyList<string> SupportedCurrencies { get; set; } = ["USD", "GBP", "CHF"];
}
