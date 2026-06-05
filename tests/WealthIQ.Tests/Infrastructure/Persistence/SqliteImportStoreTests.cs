using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Persistence;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Persistence;

public sealed class SqliteImportStoreTests
{
    private static SourceProvenance Provenance(string reference) => new()
    {
        SourceSystem = "IBKR",
        ImportFormat = "XML",
        SourceLocation = "audit/file.xml",
        SourceRecordReference = reference
    };

    private static TradeEntry Trade(AccountId account, InstrumentId instrument, string reference, int day) =>
        new(PortfolioEntryId.NewId(), account,
            new DateTimeOffset(2024, 3, day, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 3, day), Provenance(reference), instrument,
            TradeSide.Buy, new Quantity(5m),
            new Money(100m, CurrencyCode.USD), new Money(1m, CurrencyCode.USD), new Money(0m, CurrencyCode.USD));

    private static ImportBatch Batch(AccountId account, Guid batchId) =>
        new(batchId, Broker.InteractiveBrokers, Format.XML, account, "audit/file.xml",
            new DateTimeOffset(2026, 5, 30, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task PersistImport_Commits_BatchEntriesInstrumentsAccountsAndDiagnostics()
    {
        using var db = new InMemorySqlite();
        var account = new Account(AccountId.NewId(), "U123");
        var instrument = new Instrument(InstrumentId.NewId(), "US0001", "SPY", "S&P 500", 0.3m);
        var ledger = new PortfolioLedger(
            new PortfolioEntry[] { Trade(account.AccountId, instrument.InstrumentId, "T-1", 1) },
            new[] { instrument },
            new[] { account });
        var diagnostics = new[]
        {
            new ImportDiagnostic(ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.IgnoredAsset, "skipped one")
        };
        var batchId = Guid.NewGuid();

        ImportPersistCounts counts;
        await using (var ctx = db.NewContext())
        {
            var store = new SqliteImportStore(ctx, new SqliteLedgerStore(ctx));
            counts = await store.PersistImportAsync(Batch(account.AccountId, batchId), ledger, diagnostics);
        }

        Assert.Equal(new ImportPersistCounts(1, 0, 1), counts);

        await using (var ctx = db.NewContext())
        {
            Assert.Equal(1, await ctx.PortfolioEntries.CountAsync());
            Assert.Equal(1, await ctx.Instruments.CountAsync());
            Assert.Equal(1, await ctx.Accounts.CountAsync());

            var batchRow = Assert.Single(ctx.ImportBatches);
            Assert.Equal(batchId, batchRow.BatchId);
            Assert.Equal("InteractiveBrokers", batchRow.Broker);
            Assert.Equal(1, batchRow.InsertedEntries);
            Assert.Equal(0, batchRow.SkippedDuplicateEntries);

            var diagRow = Assert.Single(ctx.ImportDiagnostics);
            Assert.Equal(batchId, diagRow.BatchId);
            Assert.Equal("Warning", diagRow.Severity);
        }
    }

    [Fact]
    public async Task PersistImport_ReImportingOverlappingReferences_SkipsDuplicateEntries()
    {
        using var db = new InMemorySqlite();
        var account = new Account(AccountId.NewId(), "U123");
        var instrument = new Instrument(InstrumentId.NewId(), "US0001", "SPY", "S&P 500", 0.3m);

        PortfolioLedger First() => new(
            new PortfolioEntry[] { Trade(account.AccountId, instrument.InstrumentId, "T-1", 1) },
            new[] { instrument }, new[] { account });

        await using (var ctx = db.NewContext())
        {
            var store = new SqliteImportStore(ctx, new SqliteLedgerStore(ctx));
            await store.PersistImportAsync(Batch(account.AccountId, Guid.NewGuid()), First(), Array.Empty<ImportDiagnostic>());
        }

        // Second batch overlaps T-1 and adds T-2.
        ImportPersistCounts second;
        await using (var ctx = db.NewContext())
        {
            var overlapping = new PortfolioLedger(
                new PortfolioEntry[]
                {
                    Trade(account.AccountId, instrument.InstrumentId, "T-1", 1),
                    Trade(account.AccountId, instrument.InstrumentId, "T-2", 2)
                },
                new[] { instrument }, new[] { account });
            var store = new SqliteImportStore(ctx, new SqliteLedgerStore(ctx));
            second = await store.PersistImportAsync(Batch(account.AccountId, Guid.NewGuid()), overlapping, Array.Empty<ImportDiagnostic>());
        }

        Assert.Equal(1, second.InsertedEntries);
        Assert.Equal(1, second.SkippedDuplicateEntries);

        await using (var ctx = db.NewContext())
        {
            Assert.Equal(2, await ctx.PortfolioEntries.CountAsync());
            Assert.Equal(2, await ctx.ImportBatches.CountAsync());
        }
    }
}
