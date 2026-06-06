using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Import.Interface;
using WealthIQ.Domain.Model.General;
using WealthIQ.Tests.Application.Import.Fakes;
using Xunit;

namespace WealthIQ.Tests.Application.Import;

public sealed class StatementImportPipelineSelectionTests
{
    private static readonly AccountId TheAccountId = AccountId.NewId();
    private static readonly Account TheAccount = new(TheAccountId, "TP-1");

    private sealed class TradersPlaceImporter : IStatementImporter
    {
        public bool ImportAsyncCalled { get; private set; }

        public bool CanImport(ImportSource s) => s.Broker == Broker.TradersPlace;

        public Task<ImportResult> ImportAsync(ImportRequest r, CancellationToken ct)
        {
            ImportAsyncCalled = true;
            return Task.FromResult(new ImportResult());
        }
    }

    private sealed class IbkrImporter : IStatementImporter
    {
        public bool CanImport(ImportSource s) => s.Broker == Broker.InteractiveBrokers;

        public Task<ImportResult> ImportAsync(ImportRequest r, CancellationToken ct) =>
            throw new InvalidOperationException("Wrong importer selected.");
    }

    [Fact]
    public async Task Run_SelectsImporterByCanImport_AndIngestsDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tp-pl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Depot.csv"), "x");
        try
        {
            var tpImporter = new TradersPlaceImporter();
            var raw = new FakeRawFileStore(dir); // returns dir itself as ingestedPath
            var store = new FakeImportStore(new WealthIQ.Application.Persistence.ImportPersistCounts(0, 0, 0));
            var clock = new FixedTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));

            var pipeline = new StatementImportPipeline(
                new IStatementImporter[] { new IbkrImporter(), tpImporter },
                raw, store, clock);

            var command = new ImportStatementCommand(
                new ImportRequest
                {
                    Source = new ImportSource(Broker.TradersPlace, Format.CSV, dir),
                    AccountId = TheAccountId
                },
                TheAccount);

            var result = await pipeline.RunAsync(command);

            Assert.Equal(ImportStatus.Committed, result.Status);
            Assert.True(tpImporter.ImportAsyncCalled, "TradersPlace importer should have been selected.");
            Assert.Equal(dir, raw.SeenSourceDirectory); // directory path was passed to IngestDirectory
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Run_NoMatchingImporter_ReturnsAbortedWithFatalDiagnostic()
    {
        var raw = new FakeRawFileStore(@"C:\audit\file.xml");
        var store = new FakeImportStore(new WealthIQ.Application.Persistence.ImportPersistCounts(0, 0, 0));
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));

        // Only IBKR importer registered, but request is for TradersPlace
        var pipeline = new StatementImportPipeline(
            new IStatementImporter[] { new IbkrImporter() },
            raw, store, clock);

        var command = new ImportStatementCommand(
            new ImportRequest
            {
                Source = new ImportSource(Broker.TradersPlace, Format.CSV, @"C:\inbox\data.csv"),
                AccountId = TheAccountId
            },
            TheAccount);

        var result = await pipeline.RunAsync(command);

        Assert.Equal(ImportStatus.Aborted, result.Status);
        Assert.Single(result.Diagnostics);
        Assert.Equal(
            WealthIQ.Application.Import.Diagnostic.ImportDiagnosticSeverity.Fatal,
            result.Diagnostics[0].Severity);
        Assert.Equal(
            WealthIQ.Application.Import.Diagnostic.ImportDiagnosticCode.UnsupportedSource,
            result.Diagnostics[0].Code);
    }
}
