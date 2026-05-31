using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Interface;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Application.Import;

/// <summary>
/// Runs the v1 import flow (spec §6): ingest the raw file to the audit folder, import it to
/// canonical entries, then fail-fast (spec §8) — any diagnostic of <see cref="ImportDiagnosticSeverity.Error"/>
/// or higher aborts before any write. Otherwise the batch is persisted transactionally.
/// </summary>
public sealed class StatementImportPipeline(
    IStatementImporter importer,
    IRawFileStore rawFileStore,
    IImportStore importStore,
    TimeProvider timeProvider)
{
    public async Task<ImportPipelineResult> RunAsync(ImportStatementCommand command, CancellationToken ct = default)
    {
        var batchId = Guid.NewGuid();
        var importedAt = timeProvider.GetUtcNow();

        string storedPath;
        try
        {
            storedPath = rawFileStore.Ingest(command.Request.Source.FilePath);
        }
        catch (FileNotFoundException ex)
        {
            var diagnostic = new ImportDiagnostic(
                ImportDiagnosticSeverity.Fatal,
                ImportDiagnosticCode.InputPathNotFound,
                ex.Message);
            return new ImportPipelineResult(ImportStatus.Aborted, batchId, 0, 0, new[] { diagnostic });
        }

        var ingestedRequest = command.Request with
        {
            Source = command.Request.Source with { FilePath = storedPath }
        };

        var importResult = await importer.ImportAsync(ingestedRequest, ct);

        var hasBlocking = importResult.Diagnostics.Any(d => d.Severity >= ImportDiagnosticSeverity.Error);
        if (hasBlocking)
        {
            var failedBatch = new ImportBatch(
                batchId,
                command.Request.Source.Broker,
                command.Request.Source.Format,
                command.Request.AccountId,
                storedPath,
                importedAt,
                ImportBatchStatus.Failed);

            await importStore.PersistFailedImportAsync(failedBatch, importResult.Diagnostics, ct);

            return new ImportPipelineResult(ImportStatus.Aborted, batchId, 0, 0, importResult.Diagnostics);
        }

        var ledger = new PortfolioLedger(
            importResult.PortfolioLedger.Entries,
            importResult.PortfolioLedger.Instruments,
            new[] { command.Account });

        var batch = new ImportBatch(
            batchId,
            command.Request.Source.Broker,
            command.Request.Source.Format,
            command.Request.AccountId,
            storedPath,
            importedAt);

        var counts = await importStore.PersistImportAsync(batch, ledger, importResult.Diagnostics, ct);

        return new ImportPipelineResult(
            ImportStatus.Committed, batchId, counts.InsertedEntries, counts.SkippedDuplicateEntries, importResult.Diagnostics);
    }
}
