namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class ImportBatchRow
{
    public Guid BatchId { get; set; }
    public string Broker { get; set; } = "";
    public string Format { get; set; } = "";
    public Guid AccountId { get; set; }
    public string RawFilePath { get; set; } = "";
    public DateTimeOffset ImportedAt { get; set; }
    public int InsertedEntries { get; set; }
    public int SkippedDuplicateEntries { get; set; }
    public string Status { get; set; } = "Committed";
}
