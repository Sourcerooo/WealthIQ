namespace WealthIQ.Application.ReferenceData.Interface;

public interface ILedgerClearService
{
    /// <summary>Transactionally deletes PortfolioEntries + ImportBatches + ImportDiagnostics + Accounts.
    /// When <paramref name="purgeRawAuditFiles"/> is true, also deletes files under the audit directory.</summary>
    Task ClearLedgerAsync(bool purgeRawAuditFiles, CancellationToken ct = default);
}
