using WealthIQ.Application.Import.Diagnostic;

namespace WealthIQ.Application.ReferenceData;

/// <summary>Outcome of a dataset refresh: counts plus structured diagnostics, mirroring the import
/// philosophy (collect all diagnostics; a blocking diagnostic aborts the dataset's transaction). (spec §3)</summary>
public sealed record DataRefreshResult(
    int Added,
    int Updated,
    int Skipped,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool HasBlockingDiagnostics =>
        Diagnostics.Any(d => d.Severity >= ImportDiagnosticSeverity.Error);

    public static DataRefreshResult Empty { get; } = new(0, 0, 0, []);
}
