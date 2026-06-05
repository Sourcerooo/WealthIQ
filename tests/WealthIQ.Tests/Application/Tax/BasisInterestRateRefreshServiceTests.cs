using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;

namespace WealthIQ.Tests.Application.Tax;

public sealed class BasisInterestRateRefreshServiceTests
{
    private sealed class FakeSource(BasisInterestRateRecord? result) : IBasisInterestRateSource
    {
        public Task<BasisInterestRateRecord?> FetchAsync(int year, CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class FakeStore : IBasisInterestRateStore
    {
        public Dictionary<int, decimal> Saved = new();
        public void Upsert(int year, decimal rate) => Saved[year] = rate;
        public void Delete(int year) => Saved.Remove(year);
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task RefreshAsync_SourceReturnsRecord_Upserts()
    {
        var source = new FakeSource(new BasisInterestRateRecord(2025, 0.0253m));
        var store = new FakeStore();

        var service = new BasisInterestRateRefreshService(source, store);
        var result = await service.RefreshAsync(2025, CancellationToken.None);

        Assert.Equal(1, result.Added);
        Assert.False(result.HasBlockingDiagnostics);
        Assert.Equal(0.0253m, store.Saved[2025]);
    }

    [Fact]
    public async Task RefreshAsync_SourceReturnsNull_BlockingDiagnostic()
    {
        var source = new FakeSource(null);
        var store = new FakeStore();

        var service = new BasisInterestRateRefreshService(source, store);
        var result = await service.RefreshAsync(2025, CancellationToken.None);

        Assert.True(result.HasBlockingDiagnostics);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task SetManualAsync_AlwaysUpserts()
    {
        var source = new FakeSource(null);
        var store = new FakeStore();

        var service = new BasisInterestRateRefreshService(source, store);
        await service.SetManualAsync(2025, 0.0253m, CancellationToken.None);

        Assert.Equal(0.0253m, store.Saved[2025]);
    }
}
