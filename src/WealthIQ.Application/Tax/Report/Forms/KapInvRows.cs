using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Application.Tax.Report.Forms;

/// <summary>Which Anlage KAP-INV line each fund class maps to, formular vintage VZ 2025.
/// Captions are verbatim quotes from the form.</summary>
public static class KapInvRows
{
    public sealed record FundRow(
        TaxAssetClass Class,
        string DistributionLine, string DistributionCaption,
        string VorabLine, string VorabCaption,
        string SaleLine, string SaleCaption,
        string AltLine, string AltCaption,
        string FiktivLine, string FiktivCaption);

    public static IReadOnlyList<FundRow> All { get; } =
    [
        new(TaxAssetClass.EquityFund,
            "4", "Ausschüttungen aus Aktienfonds vor Teilfreistellung",
            "9", "Vorabpauschalen aus Aktienfonds vor Teilfreistellung",
            "14", "Einkünfte aus Verkäufen von Anteilen an Aktienfonds vor Teilfreistellung",
            "15", "Davon Gewinne aus Verkäufen von bestandsgeschützten Alt-Anteilen vor Teilfreistellung",
            "16", "Einkünfte aus fiktiven Verkäufen von Anteilen an Aktienfonds"),

        new(TaxAssetClass.MixedFund,
            "5", "Ausschüttungen aus Mischfonds vor Teilfreistellung",
            "10", "Vorabpauschalen aus Mischfonds vor Teilfreistellung",
            "17", "Einkünfte aus Verkäufen von Anteilen an Mischfonds vor Teilfreistellung",
            "18", "Davon Gewinne aus Verkäufen von bestandsgeschützten Alt-Anteilen vor Teilfreistellung",
            "19", "Einkünfte aus fiktiven Verkäufen von Anteilen an Mischfonds"),

        new(TaxAssetClass.RealEstateFund,
            "6", "Ausschüttungen aus Immobilienfonds vor Teilfreistellung",
            "11", "Vorabpauschalen aus Immobilienfonds vor Teilfreistellung",
            "20", "Einkünfte aus Verkäufen von Anteilen an Immobilienfonds vor Teilfreistellung",
            "21", "Davon Gewinne aus Verkäufen von bestandsgeschützten Alt-Anteilen vor Teilfreistellung",
            "22", "Einkünfte aus fiktiven Verkäufen von Anteilen an Immobilienfonds"),

        new(TaxAssetClass.ForeignRealEstateFund,
            "7", "Ausschüttungen aus ausländischen Immobilienfonds vor Teilfreistellung",
            "12", "Vorabpauschalen aus ausländischen Immobilienfonds vor Teilfreistellung",
            "23", "Einkünfte aus Verkäufen von Anteilen an Auslands-Immobilienfonds vor Teilfreistellung",
            "24", "Davon Gewinne aus Verkäufen von bestandsgeschützten Alt-Anteilen vor Teilfreistellung",
            "25", "Einkünfte aus fiktiven Verkäufen von Anteilen an Auslands-Immobilienfonds"),

        new(TaxAssetClass.OtherFund,
            "8", "Ausschüttungen aus sonstigen Investmentfonds",
            "13", "Vorabpauschalen aus sonstigen Investmentfonds vor Teilfreistellung",
            "26", "Einkünfte aus Verkäufen von Anteilen an sonstigen Fonds vor Teilfreistellung",
            "27", "Davon Gewinne aus Verkäufen von bestandsgeschützten Alt-Anteilen vor Teilfreistellung",
            "28", "Einkünfte aus fiktiven Verkäufen von Anteilen an sonstigen Fonds")
    ];
}
