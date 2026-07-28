using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Application.Tax.Report.Forms;

/// <summary>Form-side classification helpers for <see cref="TaxAssetClass"/>.</summary>
public static class TaxAssetClassFormExtensions
{
    /// <summary>Investment funds are declared on Anlage KAP-INV; everything else on Anlage KAP.</summary>
    public static bool IsFund(this TaxAssetClass value) => value is not (TaxAssetClass.Share or TaxAssetClass.OtherSecurity);
}
