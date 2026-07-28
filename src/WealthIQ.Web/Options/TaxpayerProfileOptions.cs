namespace WealthIQ.Web.Options;

/// <summary>
/// Identity of the taxpayer as printed in the tax report header (name, address, tax id).
/// Bound from the "TaxpayerProfile" section of appsettings.json — this is a single-user local
/// tool, so the data lives in configuration rather than in the ledger database.
/// </summary>
public sealed class TaxpayerProfileOptions
{
    public const string SectionName = "TaxpayerProfile";

    public string Name { get; set; } = "";
    public string Street { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
    public string TaxId { get; set; } = "";

    /// <summary>Single-line postal address for the report header, skipping unset parts.</summary>
    public string FormattedAddress
    {
        get
        {
            var cityLine = string.Join(" ", new[] { PostalCode, City }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var parts = new[] { Street, cityLine, Country }.Where(x => !string.IsNullOrWhiteSpace(x));
            return string.Join(", ", parts);
        }
    }
}
