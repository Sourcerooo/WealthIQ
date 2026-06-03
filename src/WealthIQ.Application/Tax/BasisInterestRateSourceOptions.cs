namespace WealthIQ.Application.Tax;

public sealed class BasisInterestRateSourceOptions
{
    public string UserAgent { get; set; } = "Mozilla/5.0";
    public string Url { get; set; } = "https://www.bundesfinanzministerium.de/Content/DE/Standardartikel/Themen/Steuern/Weitere_Steuerthemen/Abgeltungsteuer/basiszins.html";
}
