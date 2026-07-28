using System.Globalization;
using System.Text.RegularExpressions;
using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Web.Services;

/// <summary>German number/date formatting and provenance shortening for the printed tax report.</summary>
public static partial class TaxReportPrintFormat
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Num(decimal value) => value.ToString("N2", De);

    public static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", De);

    public static string Qty(decimal value) => value.ToString("0.####", De);

    public static string Percent(decimal rate) => rate.ToString("P2", De);

    /// <summary>Negative amounts get a red class so losses read at a glance.</summary>
    public static string NegClass(decimal value) => value < 0m ? "wiq-p-neg" : "";

    /// <summary>The asset class as Anlage KAP-INV Zeile 48 names it.</summary>
    public static string AssetClassLabel(TaxAssetClass? value) => value switch
    {
        TaxAssetClass.EquityFund => "Aktienfonds",
        TaxAssetClass.MixedFund => "Mischfonds",
        TaxAssetClass.RealEstateFund => "Immobilienfonds",
        TaxAssetClass.ForeignRealEstateFund => "Auslands-Immobilienfonds",
        TaxAssetClass.OtherFund => "sonstiger Fonds",
        TaxAssetClass.Share => "Aktie",
        TaxAssetClass.OtherSecurity => "sonstiges Wertpapier",
        _ => "—"
    };

    /// <summary>
    /// The statement file as the user knows it. Ledger entries record the absolute path into the
    /// raw-file store, whose file name carries the upload prefix "wealthiq-upload-&lt;guid&gt;-"
    /// in front of the broker's original file name (see Import.razor). Strip the directory and
    /// that prefix so the report cites the file the user actually downloaded.
    /// </summary>
    public static string SourceFileName(string sourceLocation)
    {
        if (string.IsNullOrWhiteSpace(sourceLocation)) return "";

        // Path.GetFileName only splits on the host separator; a statement imported on another
        // platform can carry the other one, so handle both.
        var name = sourceLocation[(sourceLocation.LastIndexOfAny(['\\', '/']) + 1)..];
        var match = UploadPrefix().Match(name);
        return match.Success ? name[match.Length..] : name;
    }

    [GeneratedRegex(@"^wealthiq-upload-[0-9a-fA-F]{32}-")]
    private static partial Regex UploadPrefix();
}
