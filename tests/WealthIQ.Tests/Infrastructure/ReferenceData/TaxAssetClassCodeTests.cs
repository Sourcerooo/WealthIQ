using WealthIQ.Domain.Enumeration;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class TaxAssetClassCodeTests
{
    [Theory]
    [InlineData("share", TaxAssetClass.Share)]
    [InlineData("other_security", TaxAssetClass.OtherSecurity)]
    [InlineData("equity_fund", TaxAssetClass.EquityFund)]
    [InlineData("mixed_fund", TaxAssetClass.MixedFund)]
    [InlineData("real_estate_fund", TaxAssetClass.RealEstateFund)]
    [InlineData("foreign_real_estate_fund", TaxAssetClass.ForeignRealEstateFund)]
    [InlineData("other_fund", TaxAssetClass.OtherFund)]
    public void Parse_KnownCode_ReturnsMatchingMember(string code, TaxAssetClass expected)
        => Assert.Equal(expected, TaxAssetClassCode.Parse(code));

    [Theory]
    [InlineData("EQUITY_FUND")]
    [InlineData("  equity_fund  ")]
    public void Parse_CodeWithDifferentCasingOrPadding_StillResolves(string code)
        => Assert.Equal(TaxAssetClass.EquityFund, TaxAssetClassCode.Parse(code));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_MissingCode_ReturnsNull(string? code)
        => Assert.Null(TaxAssetClassCode.Parse(code));

    [Fact]
    public void Parse_UnknownCode_ThrowsNamingTheCode()
    {
        var ex = Assert.Throws<ArgumentException>(() => TaxAssetClassCode.Parse("hedge_fund"));
        Assert.Contains("hedge_fund", ex.Message);
    }

    [Fact]
    public void ToCode_EveryMember_RoundTripsThroughParse()
    {
        foreach (var member in Enum.GetValues<TaxAssetClass>())
        {
            Assert.Equal(member, TaxAssetClassCode.Parse(TaxAssetClassCode.ToCode(member)));
        }
    }

    [Fact]
    public void ToCode_Null_ReturnsNull() => Assert.Null(TaxAssetClassCode.ToCode(null));
}
