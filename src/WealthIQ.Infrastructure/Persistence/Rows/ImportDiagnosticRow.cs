namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class ImportDiagnosticRow
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public string Severity { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Section { get; set; }
    public string? SourceReference { get; set; }
    public string? Field { get; set; }
}
