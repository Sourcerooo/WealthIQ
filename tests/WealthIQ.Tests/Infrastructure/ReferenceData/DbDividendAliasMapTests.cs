using Microsoft.EntityFrameworkCore;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using Xunit;

namespace WealthIQ.Tests.Infrastructure;

public sealed class DbDividendAliasMapTests
{
    private static WealthIqDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new WealthIqDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public void ResolveIsin_NormalizesAndResolves_OrReturnsNullWhenUnmapped()
    {
        using var db = NewInMemoryDb();
        db.DividendAliases.Add(new DividendAliasRow
        {
            NormalizedAlias = "VANGUARD S+P 500U.ETF DLD",
            Alias = "VANGUARD S+P 500U.ETF DLD",
            Isin = "IE00B3XXRP09"
        });
        db.SaveChanges();

        var map = new DbDividendAliasMap(db);

        Assert.Equal("IE00B3XXRP09", map.ResolveIsin("  vanguard  s+p 500u.etf dld "));
        Assert.Null(map.ResolveIsin("UNKNOWN NAME"));
    }
}
