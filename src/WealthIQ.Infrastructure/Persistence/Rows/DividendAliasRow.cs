namespace WealthIQ.Infrastructure.Persistence.Rows;

/// <summary>A user-maintained Trader's Place dividend alias → ISIN mapping. Keyed by the normalized
/// alias so lookups are stable across whitespace/case variations.</summary>
public sealed class DividendAliasRow
{
    public string NormalizedAlias { get; set; } = "";
    public string Alias { get; set; } = "";   // original, for display in the UI
    public string Isin { get; set; } = "";
}
