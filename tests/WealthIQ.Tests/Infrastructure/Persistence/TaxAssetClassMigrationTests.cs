using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Tests.Infrastructure.Persistence;

public sealed class TaxAssetClassMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"wiq-migration-{Guid.NewGuid():N}.db");

    private WealthIqDbContext CreateContext()
        => new(new DbContextOptionsBuilder<WealthIqDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options);

    [Theory]
    [InlineData("ETF_EQUITY", true, "equity_fund", true)]
    [InlineData("ETF_BOND", true, "other_fund", true)]
    [InlineData("ETF_MONEY_MARKET", true, "other_fund", true)]
    [InlineData("STOCK", false, "share", false)]
    [InlineData("ETC", true, "other_security", false)]
    [InlineData("ETC", false, "other_security", false)]
    [InlineData("ETF_METAL", true, "other_security", false)]
    [InlineData("ETF_METAL", false, "other_security", false)]
    [InlineData("SOMETHING_ELSE", true, null, true)]
    public async Task Migrate_BackfillsTaxAssetClassFromTypeAndClearsVorabpauschaleForEtcs(
        string type, bool subjectBefore, string? expectedClass, bool expectedSubjectAfter)
    {
        await using (var db = CreateContext())
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("DividendAliases");

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO InstrumentProfiles (Isin, Name, Type, Teilfreistellungsquote, SubjectToVorabpauschale) " +
                "VALUES ('TEST0000001', 'Probe', {0}, 0.0, {1});",
                type, subjectBefore ? 1 : 0);
        }

        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();

            var row = await db.InstrumentProfiles.SingleAsync(x => x.Isin == "TEST0000001");
            Assert.Equal(expectedClass, row.TaxAssetClass);
            Assert.Equal(expectedSubjectAfter, row.SubjectToVorabpauschale);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
