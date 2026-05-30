using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Persistence;

public sealed class SqliteImportAuditStoreTests
{
    private static SourceProvenance Provenance(string reference) => new()
    {
        SourceSystem = "IBKR",
        ImportFormat = "XML",
        SourceLocation = "audit/file.xml",
        SourceRecordReference = reference
    };

    private static TradeEntry Trade(AccountId account, InstrumentId instrument, string reference) =>
        new(PortfolioEntryId.NewId(), account,
            new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 3, 1), Provenance(reference), instrument,
            TradeSide.Buy, new Quantity(5m),
            new Money(100m, Currency.USD), new Money(1m, Currency.USD), new Money(0m, Currency.USD));

    [Fact]
    public async Task GetBatchesAndDiagnostics_ReturnPersistedRows()
    {
        using var db = new InMemorySqlite();
        var account = new Account(AccountId.NewId(), "U123");
        var instrument = new Instrument(InstrumentId.NewId(), "US0001", "SPY", "S&P 500", 0.3m);
        var ledger = new PortfolioLedger(
            new PortfolioEntry[] { Trade(account.AccountId, instrument.InstrumentId, "T-1") },
            new[] { instrument }, new[] { account });
        var batchId = Guid.NewGuid();
        var batch = new ImportBatch(batchId, Broker.InteractiveBrokers, Format.XML, account.AccountId,
            "audit/file.xml", new DateTimeOffset(2026, 5, 30, 9, 0, 0, TimeSpan.Zero));
        var diagnostics = new[]
        {
            new ImportDiagnostic(ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.IgnoredAsset, "skipped one", Section: "Trades")
        };

        await using (var ctx = db.NewContext())
        {
            await new SqliteImportStore(ctx, new SqliteLedgerStore(ctx)).PersistImportAsync(batch, ledger, diagnostics);
        }

        await using (var ctx = db.NewContext())
        {
            var store = new SqliteImportAuditStore(ctx);

            var batchView = Assert.Single(await store.GetBatchesAsync());
            Assert.Equal(batchId, batchView.BatchId);
            Assert.Equal("InteractiveBrokers", batchView.Broker);
            Assert.Equal(1, batchView.InsertedEntries);

            var diagView = Assert.Single(await store.GetDiagnosticsAsync());
            Assert.Equal(batchId, diagView.BatchId);
            Assert.Equal("Warning", diagView.Severity);
            Assert.Equal("Trades", diagView.Section);
        }
    }
}
