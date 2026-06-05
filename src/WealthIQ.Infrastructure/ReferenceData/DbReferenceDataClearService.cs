using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Clears individual reference datasets transactionally. Only the requested
/// table is truncated; all other data is untouched (spec §10).</summary>
public sealed class DbReferenceDataClearService(WealthIqDbContext db) : IReferenceDataClearService
{
    public async Task ClearAsync(ReferenceDataset dataset, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        switch (dataset)
        {
            case ReferenceDataset.BasisInterestRates:
                db.BasisInterestRates.RemoveRange(db.BasisInterestRates);
                break;
            case ReferenceDataset.HistoricalPrices:
                db.HistoricalPrices.RemoveRange(db.HistoricalPrices);
                break;
            case ReferenceDataset.FxRates:
                db.FxRates.RemoveRange(db.FxRates);
                break;
            case ReferenceDataset.InstrumentProfiles:
                db.InstrumentProfiles.RemoveRange(db.InstrumentProfiles);
                break;
            case ReferenceDataset.InstrumentListings:
                db.InstrumentListings.RemoveRange(db.InstrumentListings);
                break;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
