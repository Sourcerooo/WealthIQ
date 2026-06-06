using WealthIQ.Application.ReferenceData.Interface;

namespace WealthIQ.Application.ReferenceData;

/// <summary>Validates and persists dividend alias edits (add/update/delete) for the Stammdaten UI.</summary>
public sealed class DividendAliasRefreshService(IDividendAliasStore store)
{
    public async Task SetAsync(string alias, string isin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("Alias must not be blank.", nameof(alias));
        }

        if (string.IsNullOrWhiteSpace(isin))
        {
            throw new ArgumentException("ISIN must not be blank.", nameof(isin));
        }

        store.Upsert(alias, isin);
        await store.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string normalizedAlias, CancellationToken ct = default)
    {
        store.Delete(normalizedAlias);
        await store.SaveChangesAsync(ct);
    }
}
