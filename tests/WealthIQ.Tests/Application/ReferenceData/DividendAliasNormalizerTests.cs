using WealthIQ.Application.ReferenceData;
using Xunit;

namespace WealthIQ.Tests.Application.ReferenceData;

public sealed class DividendAliasNormalizerTests
{
    [Theory]
    [InlineData("VANGUARD S+P 500U.ETF DLD", "VANGUARD S+P 500U.ETF DLD")]
    [InlineData("  vanguard   s+p 500u.etf dld ", "VANGUARD S+P 500U.ETF DLD")]
    [InlineData("ISHSIV-DL T.BD20+YR DL  D", "ISHSIV-DL T.BD20+YR DL D")]
    public void Normalize_CollapsesWhitespaceAndUppercases(string input, string expected)
        => Assert.Equal(expected, DividendAliasNormalizer.Normalize(input));
}
