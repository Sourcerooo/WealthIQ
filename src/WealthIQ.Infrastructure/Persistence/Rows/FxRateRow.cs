namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class FxRateRow
{
    public DateOnly Date { get; set; }
    public string Currency { get; set; } = "";
    public decimal RateToEur { get; set; }
}
