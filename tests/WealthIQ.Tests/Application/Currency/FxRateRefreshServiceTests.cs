using WealthIQ.Application.Currency;
using WealthIQ.Application.Currency.Interface;

namespace WealthIQ.Tests.Application.Currency;

public sealed class FxRateRefreshServiceTests
{
    private sealed class FakeProvider(IReadOnlyList<FxRateRecord> records) : IFxRateProvider
    {
        public Task<IReadOnlyList<FxRateRecord>> FetchAsync(DateOnly from, DateOnly to, CancellationToken ct)
            => Task.FromResult(records);
    }

    private sealed class FakeStore : IFxRateStore
    {
        public List<FxRateRecord> Saved = new();
        public (int, int) Upsert(IReadOnlyList<FxRateRecord> records) { Saved.AddRange(records); return (records.Count, 0); }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task RefreshAsync_UpsertsFetchedRecords()
    {
        var record = new FxRateRecord(new DateOnly(2024, 12, 30), "GBP", 1.2m);
        var provider = new FakeProvider([record]);
        var store = new FakeStore();

        var service = new FxRateRefreshService(provider, store);
        var result = await service.RefreshAsync(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), CancellationToken.None);

        Assert.Equal(1, result.Added);
        Assert.False(result.HasBlockingDiagnostics);
        Assert.Single(store.Saved);
    }
}
