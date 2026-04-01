namespace WealthIQ.Domain.Model.Ledger;

public sealed record SourceProvenance
{
    public required string SourceSystem { get; init; }
    public required string ImportFormat { get; init; }
    public required string SourceLocation { get; init; }
    public required string SourceRecordReference { get; init; }
    public string? SourceSection { get; init; }
    public string? SourceLineReference { get; init; }
}
