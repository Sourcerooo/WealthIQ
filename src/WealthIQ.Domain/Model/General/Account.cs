namespace WealthIQ.Domain.Model.General;

public readonly record struct AccountId(Guid Value)
{
    public override string ToString() => Value.ToString();
    public static AccountId NewId() => new AccountId(Guid.NewGuid());
    public static explicit operator AccountId(Guid value) => new AccountId(value);
}

public sealed record Account(
    AccountId AccountId,
    string AccountNumber
)
{
    public override string ToString() => $"{AccountNumber} ({AccountId})";
};