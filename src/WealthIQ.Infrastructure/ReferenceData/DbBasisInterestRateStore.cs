using WealthIQ.Application.Tax;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Upserts Basiszins records into the <c>BasisInterestRates</c> table by Year.</summary>
public sealed class DbBasisInterestRateStore(WealthIqDbContext db) : IBasisInterestRateStore
{
    public void Upsert(int year, decimal rate)
    {
        var existing = db.BasisInterestRates.Find(year);
        if (existing is null)
        {
            db.BasisInterestRates.Add(new BasisInterestRateRow { Year = year, Rate = rate });
        }
        else
        {
            existing.Rate = rate;
        }
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct).ContinueWith(_ => { }, ct);
}
