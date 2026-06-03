namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class InstrumentProfileRow
{
    public string Isin { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Teilfreistellungsquote { get; set; }
    public bool SubjectToVorabpauschale { get; set; }
}
