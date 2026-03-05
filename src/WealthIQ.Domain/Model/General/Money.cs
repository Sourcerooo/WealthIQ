using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Domain.Model.General;

public readonly record struct Money(decimal Amount, Currency Currency)
{
    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }
    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }
    public static Money operator *(Money money, decimal factor)
        => new Money(money.Amount * factor, money.Currency);
    public static Money operator *(decimal factor, Money money)
        => money * factor;
    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException("Currency mismatch.");
        }
    }
};
