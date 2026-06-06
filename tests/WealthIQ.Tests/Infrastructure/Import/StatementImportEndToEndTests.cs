using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Import.Interface;
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Ibkr.Import;
using WealthIQ.Infrastructure.Ingest;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Tests.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Import;

public sealed class StatementImportEndToEndTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wealthiq-e2e-" + Guid.NewGuid().ToString("N"));

    private static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Import", "Fixtures", "ibkr_sample.xml");

    [Fact]
    public async Task ImportSampleStatement_PersistsLedgerBatchAndIsReloadable()
    {
        using var db = new InMemorySqlite();
        var account = new Account(AccountId.NewId(), "U5658230");
        var command = new ImportStatementCommand(
            new ImportRequest
            {
                Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, FixturePath()),
                AccountId = account.AccountId
            },
            account);

        ImportPipelineResult outcome;
        await using (var ctx = db.NewContext())
        {
            var pipeline = new StatementImportPipeline(
                new IStatementImporter[] { new IbkrStatementImporter() },
                new FileSystemRawFileStore(Path.Combine(_temp, "audit")),
                new SqliteImportStore(ctx, new SqliteLedgerStore(ctx)),
                TimeProvider.System);

            outcome = await pipeline.RunAsync(command);
        }

        foreach (var d in outcome.Diagnostics) Console.WriteLine($"{d.Severity} {d.Code} {d.Message}");

        Assert.Equal(ImportStatus.Committed, outcome.Status);
        Assert.True(outcome.InsertedEntries >= 3, $"expected >=3 entries, got {outcome.InsertedEntries}");

        await using (var ctx = db.NewContext())
        {
            Assert.Equal(outcome.InsertedEntries, await ctx.PortfolioEntries.CountAsync());
            Assert.Equal(1, await ctx.ImportBatches.CountAsync());

            var loaded = await new SqliteLedgerStore(ctx).LoadLedgerAsync();
            Assert.Equal(outcome.InsertedEntries, loaded.Entries.Count);
            Assert.Contains(loaded.Instruments, i => i.ISIN == "IE00B3XXRP09");
            Assert.Single(loaded.Accounts);
            Assert.Equal("U5658230", loaded.Accounts[0].AccountNumber);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}
