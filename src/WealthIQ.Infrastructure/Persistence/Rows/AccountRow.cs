namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class AccountRow
{
    public Guid AccountId { get; set; }
    public string AccountNumber { get; set; } = "";
}
