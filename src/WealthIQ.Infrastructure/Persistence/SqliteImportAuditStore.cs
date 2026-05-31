using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.Audit;
using WealthIQ.Application.Audit.Interface;

namespace WealthIQ.Infrastructure.Persistence;

/// <summary>Reads persisted import batches and diagnostics for the Audit page. Newest batches first.</summary>
public sealed class SqliteImportAuditStore(WealthIqDbContext db) : IImportAuditStore
{
    public async Task<IReadOnlyList<ImportBatchView>> GetBatchesAsync(CancellationToken ct = default)
    {
        var rows = await db.ImportBatches.AsNoTracking().ToListAsync(ct);
        return rows
            .OrderByDescending(x => x.ImportedAt)
            .Select(x => new ImportBatchView(
                x.BatchId, x.Broker, x.Format, x.AccountId, x.RawFilePath, x.ImportedAt,
                x.InsertedEntries, x.SkippedDuplicateEntries, x.Status))
            .ToList();
    }

    public async Task<IReadOnlyList<ImportDiagnosticView>> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        var rows = await db.ImportDiagnostics.AsNoTracking().ToListAsync(ct);

        return rows.Select(x => new ImportDiagnosticView(
            x.Id, x.BatchId, x.Severity, x.Code, x.Message, x.Section, x.SourceReference, x.Field)).ToList();
    }
}
