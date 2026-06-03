namespace WealthIQ.Application.ReferenceData;

/// <summary>Paths to the shipped reference files used for first-run seeding (spec §6).</summary>
public sealed record ReferenceDataSources(
    string BasisInterestRateCsvPath,
    string HistoricalPriceCsvPath,
    string InstrumentProfileJsonPath,
    string InstrumentListingJsonPath,
    string FxRateCsvPath);
