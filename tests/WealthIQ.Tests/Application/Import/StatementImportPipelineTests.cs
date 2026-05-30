using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Persistence;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Tests.Application.Import.Fakes;
using Xunit;

namespace WealthIQ.Tests.Application.Import;

public sealed class StatementImportPipelineTests
{
    private static readonly AccountId TheAccountId = AccountId.NewId();
    private static readonly Account TheAccount = new(TheAccountId, "U123");

    private static ImportStatementCommand Command() => new(
        new ImportRequest
        {
            Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, @"C:\inbox\statement.xml"),
            AccountId = TheAccountId
        },
        TheAccount);

    private static SourceProvenance Provenance(string reference) => new()
    {
        SourceSystem = "IBKR",
        ImportFormat = "XML",
        SourceLocation = "audit/statement.xml",
        SourceRecordReference = reference
    };

    private static TradeEntry Trade(string reference) =>
        new(PortfolioEntryId.NewId(), TheAccountId,
            new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 3, 1), Provenance(reference), InstrumentId.NewId(),
            TradeSide.Buy, new Quantity(5m),
            new Money(100m, Currency.USD), new Money(1m, Currency.USD), new Money(0m, Currency.USD));

    private static StatementImportPipeline Build(
        ImportResult importResult, FakeImportStore store, out FakeStatementImporter importer, out FakeRawFileStore raw)
    {
        importer = new FakeStatementImporter(importResult);
        raw = new FakeRawFileStore(@"C:\audit\statement.xml");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 5, 30, 9, 0, 0, TimeSpan.Zero));
        return new StatementImportPipeline(importer, raw, store, clock);
    }

    [Fact]
    public async Task Run_NoBlockingDiagnostics_IngestsImportsAndCommits()
    {
        var result = new ImportResult
        {
            PortfolioLedger = new PortfolioLedger(new PortfolioEntry[] { Trade("T-1"), Trade("T-2") }),
            Instruments = new(),
            Diagnostics = { new ImportDiagnostic(ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.IgnoredAsset, "skipped") }
        };
        var store = new FakeImportStore(new ImportPersistCounts(2, 0, 1));
        var pipeline = Build(result, store, out var importer, out var raw);

        var outcome = await pipeline.RunAsync(Command());

        Assert.Equal(ImportStatus.Committed, outcome.Status);
        Assert.Equal(2, outcome.InsertedEntries);
        Assert.Single(outcome.Diagnostics);

        // Ingest happened before import, and the importer read the ingested copy.
        Assert.Equal(@"C:\inbox\statement.xml", raw.SeenSourcePath);
        Assert.Equal(@"C:\audit\statement.xml", importer.SeenFilePath);

        // The persisted ledger carries the account, and the batch references the ingested path + clock time.
        Assert.Equal(1, store.CallCount);
        Assert.Equal(@"C:\audit\statement.xml", store.SeenBatch!.RawFilePath);
        Assert.Equal(new DateTimeOffset(2026, 5, 30, 9, 0, 0, TimeSpan.Zero), store.SeenBatch.ImportedAt);
        Assert.Single(store.SeenLedger!.Accounts);
        Assert.Equal(TheAccount, store.SeenLedger.Accounts[0]);
    }

    [Fact]
    public async Task Run_BlockingDiagnostic_AbortsWithoutPersisting()
    {
        var result = new ImportResult
        {
            PortfolioLedger = new PortfolioLedger(new PortfolioEntry[] { Trade("T-1") }),
            Diagnostics = { new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.InvalidRecord, "bad record") }
        };
        var store = new FakeImportStore(new ImportPersistCounts(0, 0, 0));
        var pipeline = Build(result, store, out _, out _);

        var outcome = await pipeline.RunAsync(Command());

        Assert.Equal(ImportStatus.Aborted, outcome.Status);
        Assert.Equal(0, outcome.InsertedEntries);
        Assert.Equal(0, store.CallCount);              // nothing persisted
        Assert.Single(outcome.Diagnostics);            // diagnostics still surfaced to the caller
    }
}
