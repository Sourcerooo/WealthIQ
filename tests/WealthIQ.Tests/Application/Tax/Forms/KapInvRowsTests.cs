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

    [Theory]
    [InlineData(TaxAssetClass.EquityFund, "4", "9", "14", "15", "16")]
    [InlineData(TaxAssetClass.MixedFund, "5", "10", "17", "18", "19")]
    [InlineData(TaxAssetClass.RealEstateFund, "6", "11", "20", "21", "22")]
    [InlineData(TaxAssetClass.ForeignRealEstateFund, "7", "12", "23", "24", "25")]
    [InlineData(TaxAssetClass.OtherFund, "8", "13", "26", "27", "28")]
    public void All_UsesTheVz2025LineNumbers(
        TaxAssetClass assetClass,
        string distribution, string vorab, string sale, string alt, string fiktiv)
    {
        var row = KapInvRows.All.Single(x => x.Class == assetClass);

        Assert.Equal(distribution, row.DistributionLine);
        Assert.Equal(vorab, row.VorabLine);
        Assert.Equal(sale, row.SaleLine);
        Assert.Equal(alt, row.AltLine);
        Assert.Equal(fiktiv, row.FiktivLine);
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
