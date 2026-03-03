namespace WealthIQ.Domain.Model.Lot;

public readonly record struct LotId(Guid Value)
{
    public override string ToString() => Value.ToString();
    public static LotId NewId() => new LotId(Guid.NewGuid());
    public static explicit operator LotId(Guid value) => new LotId(value);
};
