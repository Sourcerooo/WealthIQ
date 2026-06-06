using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Upserts/deletes dividend aliases by normalized key.</summary>
public sealed class DbDividendAliasStore(WealthIqDbContext db) : IDividendAliasStore
{
    public void Upsert(string alias, string isin)
    {
        var normalized = DividendAliasNormalizer.Normalize(alias);
        var existing = db.DividendAliases.Find(normalized);
        if (existing is null)
        {
            db.DividendAliases.Add(new DividendAliasRow
            {
                NormalizedAlias = normalized,
                Alias = alias.Trim(),
                Isin = isin.Trim()
            });
        }
        else
        {
            existing.Alias = alias.Trim();
            existing.Isin = isin.Trim();
        }
    }

    public void Delete(string normalizedAlias)
    {
        var existing = db.DividendAliases.Find(normalizedAlias);
        if (existing is not null)
        {
            db.DividendAliases.Remove(existing);
        }
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
