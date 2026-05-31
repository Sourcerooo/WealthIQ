namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class InstrumentRow
{
    public Guid InstrumentId { get; set; }
    public string ISIN { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Teilfreistellungsquote { get; set; }
}
