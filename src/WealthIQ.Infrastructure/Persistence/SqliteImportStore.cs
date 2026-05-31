using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Persistence;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Persistence.Mapping;

namespace WealthIQ.Infrastructure.Persistence;

/// <summary>
/// Persists a committed import atomically. Reuses <see cref="SqliteLedgerStore"/> on the same
/// <see cref="WealthIqDbContext"/> so all writes share one transaction and the idempotent
/// entry dedup / instrument-account upsert are defined in exactly one place.
/// </summary>
public sealed class SqliteImportStore(WealthIqDbContext db, SqliteLedgerStore ledgerStore) : IImportStore
{
    public async Task<ImportPersistCounts> PersistImportAsync(
        ImportBatch batch,
        PortfolioLedger ledger,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var batchRow = ImportBatchMapper.ToRow(batch);
        db.ImportBatches.Add(batchRow);

        var saveResult = await ledgerStore.SaveLedgerAsync(ledger, ct);
        batchRow.InsertedEntries = saveResult.InsertedEntries;
        // batchRow remains tracked by EF Core; the outer SaveChangesAsync persists these counts.
        batchRow.SkippedDuplicateEntries = saveResult.SkippedDuplicateEntries;

        foreach (var diagnostic in diagnostics)
        {
            db.ImportDiagnostics.Add(ImportDiagnosticMapper.ToRow(diagnostic, batch.BatchId));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ImportPersistCounts(saveResult.InsertedEntries, saveResult.SkippedDuplicateEntries, diagnostics.Count);
    }

    public async Task PersistFailedImportAsync(
        ImportBatch batch,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.ImportBatches.Add(ImportBatchMapper.ToRow(batch));

        foreach (var diagnostic in diagnostics)
        {
            db.ImportDiagnostics.Add(ImportDiagnosticMapper.ToRow(diagnostic, batch.BatchId));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
