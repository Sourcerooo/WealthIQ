using WealthIQ.Application.Currency;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Upserts FX rate records into the <c>FxRates</c> table by (Date, Currency).</summary>
public sealed class DbFxRateStore(WealthIqDbContext db) : IFxRateStore
{
    public (int Added, int Updated) Upsert(IReadOnlyList<FxRateRecord> records)
    {
        var added = 0;
        var updated = 0;

        foreach (var record in records)
        {
            var existing = db.FxRates.Find(record.Date, record.Currency);
            if (existing is null)
            {
                db.FxRates.Add(new FxRateRow { Date = record.Date, Currency = record.Currency, RateToEur = record.RateToEur });
                added++;
            }
            else
            {
                existing.RateToEur = record.RateToEur;
                updated++;
            }
        }

        return (added, updated);
    }

    public IReadOnlyList<string> GetStoredCurrencies() =>
        db.FxRates.Select(x => x.Currency)
            .Where(c => c != "EUR")
            .Distinct()
            .OrderBy(c => c)
            .ToList();

    public DateOnly? GetMaxStoredDate() =>
        db.FxRates.Any() ? db.FxRates.Max(x => x.Date) : (DateOnly?)null;

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct).ContinueWith(_ => { }, ct);
}
