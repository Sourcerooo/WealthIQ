using WealthIQ.Application.Tax.Report.Forms;
using WealthIQ.Domain.Enumeration;
using static WealthIQ.Tests.Application.Tax.Forms.TaxFormTestData;

namespace WealthIQ.Tests.Application.Tax.Forms;

public sealed class TaxFormReportBuilderKapInvTests
{
    [Fact]
    public void Build_EquityFundDividend_LandsOnKapInvLine4AtGrossAmount()
    {
        var report = Report(dividends:
        [
            Entry(GermanTaxEntryType.Dividend, TaxAssetClass.EquityFund, raw: 1000m, taxable: 700m)
        ]);

        var form = TaxFormReportBuilder.Build(report);

        // 1000, not 700: KAP-INV wants the amount before Teilfreistellung.
        Assert.Equal(1000m, Line(form, "KAP-INV", "4").Amount);
    }

    [Fact]
    public void Build_MixedAndOtherFundVorabpauschale_SplitsAcrossLines10And13()
    {
        var report = Report(vorab:
        [
            Entry(GermanTaxEntryType.Vorabpauschale, TaxAssetClass.MixedFund, raw: 30m, taxable: 25.5m),
            Entry(GermanTaxEntryType.Vorabpauschale, TaxAssetClass.OtherFund, raw: 12m, taxable: 12m)
        ]);

        var form = TaxFormReportBuilder.Build(report);

        Assert.Equal(30m, Line(form, "KAP-INV", "10").Amount);
        Assert.Equal(12m, Line(form, "KAP-INV", "13").Amount);
        Assert.Equal(0m, Line(form, "KAP-INV", "9").Amount);
    }

    [Fact]
    public void Build_EquityFundSales_SumIntoLine14BeforeTeilfreistellung()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 800m, taxable: 560m),
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: -200m, taxable: -140m)
        ]);

        var form = TaxFormReportBuilder.Build(report);

        Assert.Equal(600m, Line(form, "KAP-INV", "14").Amount);
    }

    [Fact]
    public void Build_NoAltAnteile_MarksLine15MutedAndZero()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 800m, taxable: 560m,
                  openedOn: new DateOnly(2019, 3, 1))
        ]);

        var form = TaxFormReportBuilder.Build(report);

        var line15 = Line(form, "KAP-INV", "15");
        Assert.Equal(0m, line15.Amount);
        Assert.True(line15.Muted);
    }

    [Fact]
    public void Build_GainOnPre2009Lot_ReportsItOnLine15AndUnmutesIt()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 800m, taxable: 560m,
                  openedOn: new DateOnly(2007, 5, 4))
        ]);

        var form = TaxFormReportBuilder.Build(report);

        var line15 = Line(form, "KAP-INV", "15");
        Assert.Equal(800m, line15.Amount);
        Assert.False(line15.Muted);
    }

    [Fact]
    public void Build_FiktiveVeraeusserungAndZwischengewinne_AreAlwaysZeroAndMuted()
    {
        var form = TaxFormReportBuilder.Build(Report());

        Assert.True(Line(form, "KAP-INV", "16").Muted);
        Assert.Equal(0m, Line(form, "KAP-INV", "16").Amount);
        Assert.True(Line(form, "KAP-INV", "29").Muted);
        Assert.Equal(0m, Line(form, "KAP-INV", "29").Amount);
    }

    [Fact]
    public void Build_EntryWithoutAssetClass_ThrowsNamingTheIsin()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, assetClass: null, raw: 100m, taxable: 100m)
        ]);

        var ex = Assert.Throws<InvalidOperationException>(() => TaxFormReportBuilder.Build(report));

        Assert.Contains("TESTISIN0001", ex.Message);
    }

    [Fact]
    public void Build_NonFundSell_DoesNotAppearOnAnyKapInvLine()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.OtherSecurity, raw: 500m, taxable: 500m)
        ]);

        var form = TaxFormReportBuilder.Build(report);

        var kapInvSaleLines = KapInvRows.All.Select(r => r.SaleLine);
        foreach (var line in kapInvSaleLines)
        {
            Assert.Equal(0m, Line(form, "KAP-INV", line).Amount);
        }
    }
}
