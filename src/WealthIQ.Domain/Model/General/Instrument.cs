namespace WealthIQ.Domain.Model.General;

public readonly record struct InstrumentId(Guid Value)
{
    public override string ToString() => Value.ToString();
    public static InstrumentId NewId() => new InstrumentId(Guid.NewGuid());
    public static explicit operator InstrumentId(Guid value) => new InstrumentId(value);
};

public sealed record Instrument(
    InstrumentId InstrumentId,
    string ISIN,
    string Symbol,
    string Name,
    decimal Teilfreistellungsquote
    )
{
    public override string ToString() => $"{Name} ({Symbol}, {ISIN})";
}
