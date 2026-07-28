using WealthIQ.Application.Tax.Report.Forms;
using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Tests.Application.Tax.Forms;

public sealed class KapInvRowsTests
{
    [Fact]
    public void All_CoversEveryFundClassExactlyOnce()
    {
        var covered = KapInvRows.All.Select(x => x.Class).ToList();

        var expected = Enum.GetValues<TaxAssetClass>().Where(x => x.IsFund()).ToList();

        Assert.Equal(expected.Count, covered.Count);
        Assert.Equal(expected.OrderBy(x => x), covered.OrderBy(x => x));
    }

    [Fact]
    public void All_UsesTheVz2025LineNumbers()
    {
        var equity = KapInvRows.All.Single(x => x.Class == TaxAssetClass.EquityFund);

        Assert.Equal("4", equity.DistributionLine);
        Assert.Equal("9", equity.VorabLine);
        Assert.Equal("14", equity.SaleLine);
        Assert.Equal("15", equity.AltLine);
        Assert.Equal("16", equity.FiktivLine);

        var other = KapInvRows.All.Single(x => x.Class == TaxAssetClass.OtherFund);

        Assert.Equal("8", other.DistributionLine);
        Assert.Equal("13", other.VorabLine);
        Assert.Equal("26", other.SaleLine);
    }

    [Fact]
    public void All_AssignsEveryLineNumberOnlyOnce()
    {
        var lines = KapInvRows.All
            .SelectMany(x => new[] { x.DistributionLine, x.VorabLine, x.SaleLine, x.AltLine, x.FiktivLine })
            .ToList();

        Assert.Equal(lines.Count, lines.Distinct().Count());
    }

    [Theory]
    [InlineData(TaxAssetClass.Share, false)]
    [InlineData(TaxAssetClass.OtherSecurity, false)]
    [InlineData(TaxAssetClass.EquityFund, true)]
    [InlineData(TaxAssetClass.MixedFund, true)]
    [InlineData(TaxAssetClass.RealEstateFund, true)]
    [InlineData(TaxAssetClass.ForeignRealEstateFund, true)]
    [InlineData(TaxAssetClass.OtherFund, true)]
    public void IsFund_SeparatesFundsFromPlainSecurities(TaxAssetClass value, bool expected)
        => Assert.Equal(expected, value.IsFund());
}
