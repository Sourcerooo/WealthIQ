namespace WealthIQ.Application.ReferenceData;

/// <summary>Row counts in each reference table after a seed run.</summary>
public sealed record ReferenceDataSeedResult(
    int BasisInterestRates,
    int HistoricalPrices,
    int InstrumentProfiles,
    int InstrumentListings,
    int FxRates);
