using WealthIQ.Application.Import.Diagnostic;

namespace WealthIQ.Application.Import;

/// <summary>
/// Result of running the import pipeline. On <see cref="ImportStatus.Aborted"/> nothing was
/// persisted and the counts are zero; <see cref="Diagnostics"/> is always the full collected set.
/// </summary>
public sealed record ImportPipelineResult(
    ImportStatus Status,
    Guid BatchId,
    int InsertedEntries,
    int SkippedDuplicateEntries,
    IReadOnlyList<ImportDiagnostic> Diagnostics);
