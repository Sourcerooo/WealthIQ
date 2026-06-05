namespace WealthIQ.Infrastructure.Persistence.Rows;

/// <summary>A provider listing for an instrument in a specific currency. Key is (Isin, Currency)
/// so the same ISIN can be held in EUR and GBP without mixing currencies (spec §4).</summary>
public sealed class InstrumentListingRow
{
    public string Isin { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderSymbol { get; set; } = "";
    public string? Exchange { get; set; }
    public string? Notes { get; set; }
}
