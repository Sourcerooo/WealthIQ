namespace WealthIQ.Infrastructure.Persistence.Rows;

/// <summary>One daily OHLCV bar for a provider listing. Key is (ProviderSymbol, Date).
/// Currency is intrinsic to the listing and stored as text (parsed to <c>Currency</c> on read,
/// mirroring <c>FxRateRow</c>). The tax engine uses <c>Close</c>, never <c>AdjustedClose</c>.</summary>
public sealed class HistoricalPriceRow
{
    public string ProviderSymbol { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Currency { get; set; } = "";
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal AdjustedClose { get; set; }
    public long Volume { get; set; }
}
