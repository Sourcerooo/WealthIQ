namespace WealthIQ.Domain.Enumeration;

/// <summary>
/// The asset class a German tax return distinguishes. Anlage KAP-INV calls it
/// "Art des Investmentfonds (Assetklasse)" and derives the Teilfreistellung rate from it;
/// non-fund securities are declared on Anlage KAP instead.
///
/// This drives ONLY the form-line mapping in the report. The tax-effective rate stays
/// <see cref="WealthIQ.Domain.Model.General.Instrument.Teilfreistellungsquote"/> — the
/// typical rate noted per member is orientation, not a source of truth.
/// </summary>
public enum TaxAssetClass
{
    /// <summary>Single share, § 20 Abs. 2 Satz 1 Nr. 1 EStG. Anlage KAP Zeile 19 and 20.</summary>
    Share,

    /// <summary>ETC, bond, certificate — not an investment fund. Anlage KAP Zeile 19.</summary>
    OtherSecurity,

    /// <summary>Aktienfonds, typically 30 % Teilfreistellung. KAP-INV Zeilen 4 / 9 / 14.</summary>
    EquityFund,

    /// <summary>Mischfonds, typically 15 %. KAP-INV Zeilen 5 / 10 / 17.</summary>
    MixedFund,

    /// <summary>Immobilienfonds, typically 60 %. KAP-INV Zeilen 6 / 11 / 20.</summary>
    RealEstateFund,

    /// <summary>Auslands-Immobilienfonds, typically 80 %. KAP-INV Zeilen 7 / 12 / 23.</summary>
    ForeignRealEstateFund,

    /// <summary>Sonstiger Investmentfonds, typically 0 %. KAP-INV Zeilen 8 / 13 / 26.</summary>
    OtherFund
}
