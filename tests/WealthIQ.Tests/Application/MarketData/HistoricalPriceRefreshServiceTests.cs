using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Application.MarketData;

public sealed class HistoricalPriceRefreshServiceTests
{
    private sealed class FakeProvider(HistoricalPriceFetchResult result) : IHistoricalPriceProvider
    {
        public DateOnly? From;
        public Task<HistoricalPriceFetchResult> FetchAsync(string s, DateOnly from, DateOnly to, CancellationToken ct)
        { From = from; return Task.FromResult(result); }
    }

    private sealed class FakeStore : IHistoricalPriceStore
    {
        public DateOnly? Max;
        public List<PriceBar> Saved = new();
        public bool Deleted;
        public IReadOnlyList<HistoricalPriceSymbol> GetConfiguredListings() => [new("VUSA.L", CurrencyCode.GBP)];
        public DateOnly? GetMaxStoredDate(string s) => Max;
        public void DeleteSymbol(string s) { Deleted = true; Saved.Clear(); }
        public (int, int) Upsert(IReadOnlyList<PriceBar> bars) { Saved.AddRange(bars); return (bars.Count, 0); }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task RefreshAsync_FetchesFromDayAfterMaxStored()
    {
        var bar = new PriceBar(new DateOnly(2024, 12, 30), "VUSA.L", CurrencyCode.GBP, 1, 1, 1, 1, 1, 1);
        var provider = new FakeProvider(new HistoricalPriceFetchResult("VUSA.L", CurrencyCode.GBP, [bar]));
        var store = new FakeStore { Max = new DateOnly(2024, 12, 1) };

        var service = new HistoricalPriceRefreshService(provider, store);
        var result = await service.RefreshAsync(new DateOnly(2024, 12, 31), forceFullReload: false, CancellationToken.None);

        Assert.Equal(new DateOnly(2024, 12, 2), provider.From);
        Assert.Equal(1, result.Added);
        Assert.False(result.HasBlockingDiagnostics);
    }

    [Fact]
    public async Task RefreshAsync_CurrencyMismatch_ProducesBlockingDiagnostic()
    {
        var bar = new PriceBar(new DateOnly(2024, 12, 30), "VUSA.L", CurrencyCode.USD, 1, 1, 1, 1, 1, 1);
        var provider = new FakeProvider(new HistoricalPriceFetchResult("VUSA.L", CurrencyCode.USD, [bar]));
        var store = new FakeStore();

        var service = new HistoricalPriceRefreshService(provider, store);
        var result = await service.RefreshAsync(new DateOnly(2024, 12, 31), forceFullReload: false, CancellationToken.None);

        Assert.True(result.HasBlockingDiagnostics);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task RefreshAsync_ForceFullReload_DeletesSymbolFirst()
    {
        var bar = new PriceBar(new DateOnly(2024, 12, 30), "VUSA.L", CurrencyCode.GBP, 1, 1, 1, 1, 1, 1);
        var provider = new FakeProvider(new HistoricalPriceFetchResult("VUSA.L", CurrencyCode.GBP, [bar]));
        var store = new FakeStore { Max = new DateOnly(2024, 12, 1) };

        var service = new HistoricalPriceRefreshService(provider, store);
        await service.RefreshAsync(new DateOnly(2024, 12, 31), forceFullReload: true, CancellationToken.None);

        Assert.True(store.Deleted);
    }

    [Fact]
    public async Task RefreshRangeAsync_SelectedSymbol_FetchesExplicitRange()
    {
        var bar = new PriceBar(new DateOnly(2022, 6, 15), "VUSA.L", CurrencyCode.GBP, 1, 1, 1, 1, 1, 1);
        var provider = new FakeProvider(new HistoricalPriceFetchResult("VUSA.L", CurrencyCode.GBP, [bar]));
        var store = new FakeStore();

        var service = new HistoricalPriceRefreshService(provider, store);
        var result = await service.RefreshRangeAsync(["VUSA.L"], new DateOnly(2020, 1, 1), new DateOnly(2024, 12, 31), forceFullReload: false, CancellationToken.None);

        Assert.Equal(new DateOnly(2020, 1, 1), provider.From);
        Assert.Equal(1, result.Added);
        Assert.False(result.HasBlockingDiagnostics);
    }
}
