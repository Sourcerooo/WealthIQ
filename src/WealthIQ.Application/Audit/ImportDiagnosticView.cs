namespace WealthIQ.Application.Audit;

/// <summary>One persisted diagnostic, linked to its batch, for the Audit page.</summary>
public sealed record ImportDiagnosticView(
    Guid Id,
    Guid BatchId,
    string Severity,
    string Code,
    string Message,
    string? Section,
    string? SourceReference,
    string? Field);
