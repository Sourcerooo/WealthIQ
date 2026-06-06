using WealthIQ.Application.Currency;
using WealthIQ.Application.Currency.Interface;

namespace WealthIQ.Tests.Application.Currency;

public sealed class FxRateRefreshServiceTests
{
    private sealed class FakeProvider(IReadOnlyList<FxRateRecord> records) : IFxRateProvider
    {
        public Task<IReadOnlyList<FxRateRecord>> FetchAsync(
            DateOnly from, DateOnly to, IReadOnlyCollection<string>? currencies, CancellationToken ct)
            => Task.FromResult(records);
    }

    private sealed class FakeStore : IFxRateStore
    {
        public List<FxRateRecord> Saved = new();
        public List<string> Stored = new();
        public DateOnly? MaxDate;
        public (int, int) Upsert(IReadOnlyList<FxRateRecord> records) { Saved.AddRange(records); return (records.Count, 0); }
        public IReadOnlyList<string> GetStoredCurrencies() => Stored;
        public DateOnly? GetMaxStoredDate() => MaxDate;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CapturingProvider : IFxRateProvider
    {
        public DateOnly? From; public DateOnly? To; public IReadOnlyCollection<string>? Currencies;
        private readonly IReadOnlyList<FxRateRecord> _records;
        public CapturingProvider(IReadOnlyList<FxRateRecord> records) => _records = records;
        public Task<IReadOnlyList<FxRateRecord>> FetchAsync(
            DateOnly from, DateOnly to, IReadOnlyCollection<string>? currencies, CancellationToken ct)
        { From = from; To = to; Currencies = currencies; return Task.FromResult(_records); }
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

    [Fact]
    public async Task RefreshIncrementalAsync_FetchesFromDayAfterMaxStoredDate()
    {
        var provider = new CapturingProvider([new FxRateRecord(new DateOnly(2025, 1, 2), "USD", 0.9m)]);
        var store = new FakeStore { MaxDate = new DateOnly(2025, 1, 1), Stored = { "USD", "GBP" } };

        var service = new FxRateRefreshService(provider, store);
        var result = await service.RefreshIncrementalAsync(new DateOnly(2025, 1, 31), CancellationToken.None);

        Assert.Equal(new DateOnly(2025, 1, 2), provider.From);
        Assert.Equal(new DateOnly(2025, 1, 31), provider.To);
        Assert.Contains("USD", provider.Currencies!);
        Assert.Contains("GBP", provider.Currencies!);
        Assert.Equal(1, result.Added);
    }

    [Fact]
    public async Task AddCurrencyAsync_FetchesOnlyThatCurrency()
    {
        var provider = new CapturingProvider([new FxRateRecord(new DateOnly(2024, 6, 1), "JPY", 0.006m)]);
        var store = new FakeStore();

        var service = new FxRateRefreshService(provider, store);
        var result = await service.AddCurrencyAsync("JPY", new DateOnly(2020, 1, 1), new DateOnly(2024, 12, 31), CancellationToken.None);

        Assert.Equal(new[] { "JPY" }, provider.Currencies);
        Assert.Equal(1, result.Added);
        Assert.Single(store.Saved);
    }
}
