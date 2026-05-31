using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Application.Tax.Report;

/// <summary>A single tax year: headline summary plus the underlying tax entries grouped by kind (for the drill-down grids).</summary>
public sealed record AnnualTaxReport(
    int Year,
    TaxReportSummary Summary,
    IReadOnlyList<GermanTaxEntry> Sells,
    IReadOnlyList<GermanTaxEntry> Dividends,
    IReadOnlyList<GermanTaxEntry> Interest,
    IReadOnlyList<GermanTaxEntry> WithholdingTaxes,
    IReadOnlyList<GermanTaxEntry> Vorabpauschale);
