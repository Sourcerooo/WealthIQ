namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class InstrumentProfileRow
{
    public string Isin { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Teilfreistellungsquote { get; set; }
    public bool SubjectToVorabpauschale { get; set; }

    /// <summary>Snake_case code of <see cref="WealthIQ.Domain.Enumeration.TaxAssetClass"/>;
    /// <c>null</c> when the profile has not been classified yet. See <c>TaxAssetClassCode</c>.</summary>
    public string? TaxAssetClass { get; set; }
}
