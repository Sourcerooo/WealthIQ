using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Application.Persistence.Interface;

/// <summary>
/// Persists a committed import in a single transaction: the batch record, the ledger
/// (entries idempotent on (SourceSystem, SourceRecordReference); instruments/accounts upserted),
/// and the diagnostics linked to the batch. Rolls back entirely on failure (spec §8).
/// </summary>
public interface IImportStore
{
    Task<ImportPersistCounts> PersistImportAsync(
        ImportBatch batch,
        PortfolioLedger ledger,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        CancellationToken ct = default);
}
