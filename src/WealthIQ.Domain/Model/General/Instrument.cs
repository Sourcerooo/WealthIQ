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
    decimal Teilfreistellungsquote)
{
    /// <summary>Instrument classification from the profile (e.g. "ETF_EQUITY"). Empty until enriched.</summary>
    public string Type { get; init; } = "";

    /// <summary>Whether §18 InvStG Vorabpauschale applies. Set explicitly by the profile; there is no inference.
    /// A held instrument with no profile is a blocking error at tax replay (spec §2, §6.4).
    /// <c>null</c> = not yet enriched / no profile on file.</summary>
    public bool? SubjectToVorabpauschale { get; init; }

    public override string ToString() => $"{Name} ({Symbol}, {ISIN})";
}
