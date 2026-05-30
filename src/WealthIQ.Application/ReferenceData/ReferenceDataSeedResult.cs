namespace WealthIQ.Application.ReferenceData;

/// <summary>Row counts in each reference table after a seed run.</summary>
public sealed record ReferenceDataSeedResult(
    int BasisInterestRates,
    int YearEndPrices,
    int InstrumentProfiles,
    int FxRates);
