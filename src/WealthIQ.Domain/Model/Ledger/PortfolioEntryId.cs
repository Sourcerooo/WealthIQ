namespace WealthIQ.Domain.Model.Ledger;

public readonly record struct PortfolioEntryId(Guid Value)
{
    public override string ToString() => Value.ToString();
    public static PortfolioEntryId NewId() => new(Guid.NewGuid());
    public static explicit operator PortfolioEntryId(Guid value) => new(value);
}
