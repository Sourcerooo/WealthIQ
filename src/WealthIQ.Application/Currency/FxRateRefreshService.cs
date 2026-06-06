using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.ReferenceData;

namespace WealthIQ.Application.Currency;

/// <summary>Fetches FX rates from [from, to] via the provider, upserts into the store by (Date, Currency).</summary>
public sealed class FxRateRefreshService(IFxRateProvider provider, IFxRateStore store)
{
    public async Task<DataRefreshResult> RefreshAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var records = await provider.FetchAsync(from, to, null, ct);
        var (added, updated) = store.Upsert(records);
        await store.SaveChangesAsync(ct);
        return new DataRefreshResult(added, updated, 0, []);
    }
}
