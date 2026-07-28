using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>
/// Translates between <see cref="TaxAssetClass"/> and the snake_case code stored in
/// instruments.json and in the InstrumentProfiles table. A stable code keeps reference files
/// readable and survives renaming the enum members.
/// </summary>
public static class TaxAssetClassCode
{
    private static readonly Dictionary<string, TaxAssetClass> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["share"] = TaxAssetClass.Share,
        ["other_security"] = TaxAssetClass.OtherSecurity,
        ["equity_fund"] = TaxAssetClass.EquityFund,
        ["mixed_fund"] = TaxAssetClass.MixedFund,
        ["real_estate_fund"] = TaxAssetClass.RealEstateFund,
        ["foreign_real_estate_fund"] = TaxAssetClass.ForeignRealEstateFund,
        ["other_fund"] = TaxAssetClass.OtherFund
    };

    private static readonly Dictionary<TaxAssetClass, string> ToCodeMap =
        ByCode.ToDictionary(x => x.Value, x => x.Key);

    /// <summary>Empty/absent input means "not classified" and stays <c>null</c>; an unknown code
    /// is a data error and fails loudly rather than defaulting to a category.</summary>
    public static TaxAssetClass? Parse(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        if (ByCode.TryGetValue(code.Trim(), out var value))
        {
            return value;
        }

        throw new ArgumentException(
            $"Unknown tax asset class code '{code}'. Expected one of: {string.Join(", ", ByCode.Keys)}.",
            nameof(code));
    }

    public static string? ToCode(TaxAssetClass? value)
        => value is null ? null : ToCodeMap[value.Value];
}
