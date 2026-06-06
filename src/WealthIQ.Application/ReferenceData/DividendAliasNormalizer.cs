using System.Text.RegularExpressions;

namespace WealthIQ.Application.ReferenceData;

/// <summary>Canonicalizes dividend alias strings so trivial whitespace/case differences still match.</summary>
public static partial class DividendAliasNormalizer
{
    public static string Normalize(string alias)
        => WhitespaceRegex().Replace((alias ?? string.Empty).Trim(), " ").ToUpperInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
