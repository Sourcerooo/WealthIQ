namespace WealthIQ.Application.ReferenceData.Interface;

public enum ReferenceDataset
{
    BasisInterestRates,
    HistoricalPrices,
    FxRates,
    InstrumentProfiles,
    InstrumentListings
}

public interface IReferenceDataClearService
{
    Task ClearAsync(ReferenceDataset dataset, CancellationToken ct = default);
}
