namespace WealthIQ.Application.ReferenceData;

/// <summary>A dividend alias row for display/editing in the Stammdaten UI.</summary>
public sealed record DividendAliasView(string NormalizedAlias, string Alias, string Isin);
