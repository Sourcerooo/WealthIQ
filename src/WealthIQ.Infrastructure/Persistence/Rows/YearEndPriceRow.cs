namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class YearEndPriceRow
{
    public int Year { get; set; }
    public string Isin { get; set; } = "";
    public decimal PriceEur { get; set; }
}
