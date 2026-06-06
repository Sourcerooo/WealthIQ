using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.ReferenceData;

namespace WealthIQ.Application.Currency;

/// <summary>Fetches FX rates via the provider, upserts into the store by (Date, Currency).</summary>
public sealed class FxRateRefreshService(IFxRateProvider provider, IFxRateStore store)
{
    private static readonly string[] DefaultCurrencies = ["USD", "GBP", "CHF"];

    /// <summary>Explicit [from, to] refresh of the provider's default currency set.</summary>
    public async Task<DataRefreshResult> RefreshAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var records = await provider.FetchAsync(from, to, null, ct);
        var (added, updated) = store.Upsert(records);
        await store.SaveChangesAsync(ct);
        return new DataRefreshResult(added, updated, 0, []);
    }

    /// <summary>Incremental refresh of every currently-tracked currency
    /// (stored currencies ∪ defaults) from the day after the latest stored date through asOf.</summary>
    public async Task<DataRefreshResult> RefreshIncrementalAsync(DateOnly asOf, CancellationToken ct)
    {
        var tracked = store.GetStoredCurrencies()
            .Concat(DefaultCurrencies)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var from = store.GetMaxStoredDate()?.AddDays(1) ?? asOf.AddYears(-5);
        if (from > asOf)
        {
            return new DataRefreshResult(0, 0, 1, []);
        }

        var records = await provider.FetchAsync(from, asOf, tracked, ct);
        var (added, updated) = store.Upsert(records);
        await store.SaveChangesAsync(ct);
        return new DataRefreshResult(added, updated, 0, []);
    }

    /// <summary>Backfills a single currency over [from, to]. Once stored it becomes part of the
    /// tracked set picked up by <see cref="RefreshIncrementalAsync"/>.</summary>
    public async Task<DataRefreshResult> AddCurrencyAsync(string currency, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var records = await provider.FetchAsync(from, to, new[] { currency }, ct);
        var (added, updated) = store.Upsert(records);
        await store.SaveChangesAsync(ct);
        return new DataRefreshResult(added, updated, 0, []);
    }
}
