using WealthIQ.Application.Tax.Report.Forms;
using WealthIQ.Domain.Enumeration;

using static WealthIQ.Tests.Application.Tax.Forms.TaxFormTestData;

namespace WealthIQ.Tests.Application.Tax.Forms;

/// <summary>Tests for the Anlage-KAP section (non-fund income) and the certified-account
/// (domestic withholding) route of <see cref="TaxFormReportBuilder"/> (Task 7).</summary>
public sealed class TaxFormReportBuilderKapTests
{
    [Fact]
    public void Build_InterestAndNonFundIncome_SumIntoKapLine19()
    {
        var report = Report(
            sells: [Entry(GermanTaxEntryType.Sell, TaxAssetClass.OtherSecurity, raw: 500m, taxable: 500m)],
            dividends: [Entry(GermanTaxEntryType.Dividend, TaxAssetClass.Share, raw: 120m, taxable: 120m)],
            interest: [Entry(GermanTaxEntryType.Interest, TaxAssetClass.OtherSecurity, raw: 30m, taxable: 30m)]);

        var form = TaxFormReportBuilder.Build(report, ForeignBroker);

        Assert.Equal(650m, Line(form, "KAP", "19").Amount);
    }

    [Fact]
    public void Build_FundIncome_IsExcludedFromKapLine19()
    {
        var report = Report(
            sells: [Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 5000m, taxable: 3500m)],
            interest: [Entry(GermanTaxEntryType.Interest, TaxAssetClass.OtherSecurity, raw: 30m, taxable: 30m)]);

        var form = TaxFormReportBuilder.Build(report, ForeignBroker);

        // Fund income belongs on KAP-INV; Zeile 19 must not double-count it.
        Assert.Equal(30m, Line(form, "KAP", "19").Amount);
    }

    [Fact]
    public void Build_ShareGains_AlsoAppearOnKapLine20()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.Share, raw: 400m, taxable: 400m),
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.OtherSecurity, raw: 100m, taxable: 100m)
        ]);

        var form = TaxFormReportBuilder.Build(report, ForeignBroker);

        Assert.Equal(400m, Line(form, "KAP", "20").Amount);
    }

    [Fact]
    public void Build_Losses_AreReportedPositivelyAndSplitByPot()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.Share, raw: -250m, taxable: -250m),
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.OtherSecurity, raw: -80m, taxable: -80m)
        ]);

        var form = TaxFormReportBuilder.Build(report, ForeignBroker);

        Assert.Equal(80m, Line(form, "KAP", "22").Amount);   // Topf 2, ohne Aktien
        Assert.Equal(250m, Line(form, "KAP", "23").Amount);  // Topf 1, Aktien
    }

    [Fact]
    public void Build_ForeignWithholding_LandsOnKapLine41()
    {
        // A foreign broker, so this stays on the KAP-INV / Zeile-19 route.
        var report = Report(
            withholding: [Entry(GermanTaxEntryType.WithholdingTax, TaxAssetClass.Share, raw: 45m, taxable: 0m) with { ForeignWithholdingTax = 45m }]);

        var form = TaxFormReportBuilder.Build(report, ForeignBroker);

        Assert.Equal(45m, Line(form, "KAP", "41").Amount);
        // Nothing was withheld domestically, so Zeile 37 is present but greyed out at zero.
        Assert.Equal(0m, Line(form, "KAP", "37").Amount);
        Assert.True(Line(form, "KAP", "37").Muted);
    }

    [Fact]
    public void Build_DomesticPayingAgent_SwitchesToTheCertifiedRoute()
    {
        var report = Report(
            sells: [Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 1000m, taxable: 700m)],
            withheldKest: 40m);

        var form = TaxFormReportBuilder.Build(report, DomesticBroker);

        Assert.True(form.DomesticWithholding);
        Assert.DoesNotContain(form.Sections, s => s.Form == "KAP-INV");
        Assert.Equal(700m, Line(form, "KAP", "7").Amount);   // nach Teilfreistellung, lt. Bescheinigung
    }

    [Fact]
    public void Build_DomesticPayingAgentWithZeroWithheldKest_StillUsesTheCertifiedRoute()
    {
        // The Sparer-Pauschbetrag or the broker's own Verlustverrechnung can drive withholding to
        // zero. The Steuerbescheinigung still exists, so the income must NOT be re-declared on
        // KAP-INV. Routing is by broker, not by the withheld amount.
        var report = Report(
            dividends: [Entry(GermanTaxEntryType.Dividend, TaxAssetClass.EquityFund, raw: 500m, taxable: 350m)],
            withheldKest: 0m);

        var form = TaxFormReportBuilder.Build(report, DomesticBroker);

        Assert.True(form.DomesticWithholding);
        Assert.DoesNotContain(form.Sections, s => s.Form == "KAP-INV");
        Assert.Equal(350m, Line(form, "KAP", "7").Amount);
    }

    [Fact]
    public void Build_DomesticPayingAgent_IsMatchedCaseInsensitively()
    {
        var report = Report(interest: [Entry(GermanTaxEntryType.Interest, TaxAssetClass.OtherSecurity, raw: 10m, taxable: 10m)]);

        Assert.True(TaxFormReportBuilder.Build(report, "tradersplace").DomesticWithholding);
        Assert.True(TaxFormReportBuilder.Build(report, "TRADERSPLACE").DomesticWithholding);
    }

    [Fact]
    public void Build_ForeignBroker_KeepsTheKapInvRoute()
    {
        var report = Report(
            sells: [Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 1000m, taxable: 700m)]);

        var form = TaxFormReportBuilder.Build(report, ForeignBroker);

        Assert.False(form.DomesticWithholding);
        Assert.Contains(form.Sections, s => s.Form == "KAP-INV");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SomeNewBroker")]
    public void Build_UnknownOrEmptySourceSystem_TakesTheForeignRoute(string sourceSystem)
    {
        var report = Report(
            sells: [Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 1000m, taxable: 700m)]);

        var form = TaxFormReportBuilder.Build(report, sourceSystem);

        Assert.False(form.DomesticWithholding);
        Assert.Contains(form.Sections, s => s.Form == "KAP-INV");
    }

    [Fact]
    public void Build_ForeignBrokerWithWithheldKest_StaysOnKapInvAndFlagsTheInconsistency()
    {
        var report = Report(
            sells: [Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 1000m, taxable: 700m)],
            withheldKest: 40m);

        var form = TaxFormReportBuilder.Build(report, ForeignBroker);

        // Withheld KESt no longer flips the route ...
        Assert.False(form.DomesticWithholding);
        Assert.Contains(form.Sections, s => s.Form == "KAP-INV");
        // ... but it is a real inconsistency, so the KAP section says so and Zeile 37 keeps the amount.
        var note = form.Sections.Single(s => s.Form == "KAP").Note ?? "";
        Assert.Contains("inländische Zahlstelle", note);
        Assert.Contains("Steuerbescheinigung", note);
        Assert.Equal(40m, Line(form, "KAP", "37").Amount);
        Assert.False(Line(form, "KAP", "37").Muted);
    }

    [Fact]
    public void Build_ForeignBrokerWithoutWithheldKest_LeavesTheKapNoteClean()
    {
        var report = Report(
            sells: [Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 1000m, taxable: 700m)]);

        var form = TaxFormReportBuilder.Build(report, ForeignBroker);

        var note = form.Sections.Single(s => s.Form == "KAP").Note ?? "";
        Assert.DoesNotContain("Achtung", note);
    }

    [Fact]
    public void Build_VorabpauschaleOnANonFund_ThrowsNamingTheIsin()
    {
        // A Vorabpauschale can only arise on an investment fund; a Share/OtherSecurity entry has no
        // KAP-INV line to land on and would silently vanish from the form.
        var report = Report(vorab:
        [
            Entry(GermanTaxEntryType.Vorabpauschale, TaxAssetClass.OtherSecurity, raw: 12m, taxable: 12m)
        ]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => TaxFormReportBuilder.Build(report, ForeignBroker));

        Assert.Contains("TESTISIN0001", ex.Message);
        Assert.Contains("investment fund", ex.Message);
    }
}
