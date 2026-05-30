using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Application.Persistence.Interface;

/// <summary>
/// Stores and loads the canonical portfolio ledger.
/// Saving is idempotent on (SourceSystem, SourceRecordReference) so re-importing
/// overlapping statements never duplicates entries.
/// </summary>
public interface ILedgerStore
{
    Task<LedgerSaveResult> SaveLedgerAsync(PortfolioLedger ledger, CancellationToken ct = default);

    Task<PortfolioLedger> LoadLedgerAsync(CancellationToken ct = default);
}
