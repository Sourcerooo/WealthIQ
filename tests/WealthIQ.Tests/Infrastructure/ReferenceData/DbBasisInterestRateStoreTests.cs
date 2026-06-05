using Microsoft.EntityFrameworkCore;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class DbBasisInterestRateStoreTests
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
    public async Task Delete_RemovesExistingYear()
    {
        using var db = NewDb();
        var store = new DbBasisInterestRateStore(db);
        store.Upsert(2023, 0.0255m);
        store.Upsert(2024, 0.0253m);
        await store.SaveChangesAsync(CancellationToken.None);

        store.Delete(2023);
        await store.SaveChangesAsync(CancellationToken.None);

        Assert.Null(await db.BasisInterestRates.FindAsync(2023));
        Assert.NotNull(await db.BasisInterestRates.FindAsync(2024));
    }

    [Fact]
    public async Task Delete_MissingYear_NoOp()
    {
        using var db = NewDb();
        var store = new DbBasisInterestRateStore(db);

        store.Delete(1999);
        await store.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(0, await db.BasisInterestRates.CountAsync());
    }
}
