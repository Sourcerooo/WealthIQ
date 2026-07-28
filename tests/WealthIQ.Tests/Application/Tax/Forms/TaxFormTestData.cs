using WealthIQ.Application.Tax.Report;
using WealthIQ.Application.Tax.Report.Forms;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Tests.Application.Tax.Forms;

/// <summary>Shared builders for <see cref="TaxFormReportBuilder"/> tests (Tasks 6 and 7), so both
/// test classes construct entries/reports/line lookups the same way instead of duplicating them.</summary>
internal static class TaxFormTestData
{
    /// <summary>A broker that is NOT an inländische Zahlstelle — the KAP-INV route.</summary>
    internal const string ForeignBroker = "IBKR";

    /// <summary>A broker on <see cref="DomesticPayingAgents.SourceSystems"/> — the Zeile-7 route.</summary>
    internal const string DomesticBroker = "TradersPlace";

    internal static GermanTaxEntry Entry(
        GermanTaxEntryType type, TaxAssetClass? assetClass,
        decimal raw, decimal taxable, DateOnly? openedOn = null)
        => new(
            Year: 2025,
            Date: new DateOnly(2025, 6, 1),
            Type: type,
            Symbol: "SYM",
            Isin: "TESTISIN0001",
            RawAmount: raw,
            TaxableAmount: taxable,
            OpenedOn: openedOn ?? new DateOnly(2020, 1, 1),
            AssetClass: assetClass,
            InstrumentName: "Testfonds");

    internal static AnnualTaxReport Report(
        IReadOnlyList<GermanTaxEntry>? sells = null,
        IReadOnlyList<GermanTaxEntry>? dividends = null,
        IReadOnlyList<GermanTaxEntry>? interest = null,
        IReadOnlyList<GermanTaxEntry>? withholding = null,
        IReadOnlyList<GermanTaxEntry>? vorab = null,
        decimal withheldKest = 0m)
        => new(
            2025,
            new TaxReportSummary(0m, 0m, 0m, 0m, 0m, 0m, withheldKest),
            sells ?? [], dividends ?? [], interest ?? [], withholding ?? [], vorab ?? []);

    internal static TaxFormLine Line(TaxFormReport report, string form, string line)
        => report.Sections.Where(s => s.Form == form).SelectMany(s => s.Lines).Single(l => l.Line == line);
}
