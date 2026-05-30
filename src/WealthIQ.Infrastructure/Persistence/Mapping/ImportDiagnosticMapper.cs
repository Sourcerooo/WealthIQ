using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.Persistence.Mapping;

public static class ImportDiagnosticMapper
{
    public static ImportDiagnosticRow ToRow(ImportDiagnostic diagnostic, Guid batchId) => new()
    {
        Id = Guid.NewGuid(),
        BatchId = batchId,
        Severity = diagnostic.Severity.ToString(),
        Code = diagnostic.Code.ToString(),
        Message = diagnostic.Message,
        Section = diagnostic.Section,
        SourceReference = diagnostic.SourceReference,
        Field = diagnostic.Field
    };

    public static ImportDiagnostic ToDomain(ImportDiagnosticRow row) => new(
        Enum.Parse<ImportDiagnosticSeverity>(row.Severity),
        Enum.Parse<ImportDiagnosticCode>(row.Code),
        row.Message,
        row.Section,
        row.SourceReference,
        row.Field);
}
