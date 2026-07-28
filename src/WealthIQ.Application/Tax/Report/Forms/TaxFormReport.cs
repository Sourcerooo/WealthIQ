namespace WealthIQ.Application.Tax.Report.Forms;

/// <summary>One line of a German tax form, ready to be typed into the tax software.</summary>
/// <param name="Line">The form's line number as printed on it, e.g. "14". Empty for memo rows.</param>
/// <param name="Caption">The form's own wording for that line.</param>
/// <param name="Amount">EUR. For Anlage KAP-INV always BEFORE Teilfreistellung — the tax office
/// applies the quota itself, so entering a reduced amount would cut it twice.</param>
/// <param name="Nachweis">Cross-reference to the Einzelnachweis backing this figure, e.g. "A".</param>
/// <param name="Muted">The line belongs to the form but WealthIQ always reports 0 for it — rendered
/// greyed out so it is visibly "checked and zero" rather than forgotten.</param>
public sealed record TaxFormLine(
    string Line,
    string Caption,
    decimal Amount,
    string Nachweis = "",
    bool Muted = false);

/// <summary>A block of lines belonging to one form section.</summary>
public sealed record TaxFormSection(
    string Form,
    string Title,
    string? Note,
    IReadOnlyList<TaxFormLine> Lines);

/// <summary>One account-year rendered as the form lines it maps to (spec §3).</summary>
/// <param name="DomesticWithholding">The broker already withheld German KESt, so the income is
/// declared on Anlage KAP Zeile 7 from the Steuerbescheinigung instead of on KAP-INV.</param>
public sealed record TaxFormReport(
    int Year,
    bool DomesticWithholding,
    IReadOnlyList<TaxFormSection> Sections)
{
    /// <summary>Line numbers shift between assessment years; this report is calibrated on VZ 2025.</summary>
    public const string Vintage =
        "Formularstand VZ 2025 — Zeilennummern älterer Jahrgänge können abweichen. Die Beträge sind jahresunabhängig.";
}
