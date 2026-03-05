namespace WealthIQ.Application.Import.Diagnostic;

public sealed record ImportDiagnostic(
    ImportDiagnosticSeverity Severity,
    ImportDiagnosticCode Code,
    string Message,
    string? Section = null,
    string? SourceReference = null,
    string? Field = null);

