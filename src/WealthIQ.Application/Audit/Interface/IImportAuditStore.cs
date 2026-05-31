namespace WealthIQ.Application.Audit.Interface;

/// <summary>Read-only access to persisted import batches and diagnostics (spec §9 Diagnostics/Audit).</summary>
public interface IImportAuditStore
{
    Task<IReadOnlyList<ImportBatchView>> GetBatchesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ImportDiagnosticView>> GetDiagnosticsAsync(CancellationToken ct = default);
}
