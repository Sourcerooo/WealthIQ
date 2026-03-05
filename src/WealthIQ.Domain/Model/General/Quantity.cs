namespace WealthIQ.Domain.Model.General;

public readonly record struct Quantity(decimal Value)
{
    public static Quantity operator +(Quantity left, Quantity right)
    {
        return new Quantity(left.Value + right.Value);
    }
    public static Quantity operator -(Quantity left, Quantity right)
    {
        return new Quantity(left.Value - right.Value);
    }
    public static Quantity operator *(Quantity Quantity, decimal factor)
        => new Quantity(Quantity.Value * factor);
    public static Quantity operator *(decimal factor, Quantity Quantity)
        => Quantity * factor;
};
