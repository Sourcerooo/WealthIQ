namespace WealthIQ.Application.Import.Diagnostic;

public enum ImportDiagnosticCode
{
    UnsupportedSource,
    InputPathNotFound,
    FileReadFailed,
    InvalidRecord,
    IgnoredAsset,
    CancellationRemoved
}
