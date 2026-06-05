using Microsoft.EntityFrameworkCore;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class DbDataRefreshLogTests
{
    private static WealthIqDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>().UseSqlite("Data Source=:memory:").Options;
        var db = new WealthIqDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task RecordAndGet_UpsertsByDataset()
    {
        using var db = NewDb();
        var log = new DbDataRefreshLog(db);
        var t1 = new DateTimeOffset(2024, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2024, 6, 2, 10, 0, 0, TimeSpan.Zero);

        await log.RecordAsync("HistoricalPrices", t1, null);
        await log.RecordAsync("HistoricalPrices", t2, "incremental");

        var result = await log.GetLastRefreshedAsync("HistoricalPrices");
        Assert.Equal(t2, result);
    }

    [Fact]
    public async Task GetLastRefreshedAsync_MissingDataset_ReturnsNull()
    {
        using var db = NewDb();
        var log = new DbDataRefreshLog(db);
        Assert.Null(await log.GetLastRefreshedAsync("DoesNotExist"));
    }
}
