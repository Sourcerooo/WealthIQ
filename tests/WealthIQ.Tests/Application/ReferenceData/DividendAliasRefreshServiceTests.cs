using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using Xunit;

namespace WealthIQ.Tests.Application.ReferenceData;

public sealed class DividendAliasRefreshServiceTests
{
    private sealed class FakeStore : IDividendAliasStore
    {
        public readonly List<(string Alias, string Isin)> Upserts = new();
        public readonly List<string> Deletes = new();
        public int Saves;
        public void Upsert(string alias, string isin) => Upserts.Add((alias, isin));
        public void Delete(string normalizedAlias) => Deletes.Add(normalizedAlias);
        public Task SaveChangesAsync(CancellationToken ct) { Saves++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task SetAsync_RejectsBlankAliasOrIsin()
    {
        var store = new FakeStore();
        var service = new DividendAliasRefreshService(store);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(" ", "IE00B3XXRP09"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("ALIAS", " "));
        Assert.Empty(store.Upserts);
    }

    [Fact]
    public async Task SetAsync_UpsertsAndSaves()
    {
        var store = new FakeStore();
        var service = new DividendAliasRefreshService(store);

        await service.SetAsync("VANGUARD S+P 500U.ETF DLD", "IE00B3XXRP09");

        Assert.Single(store.Upserts);
        Assert.Equal(1, store.Saves);
    }
}
