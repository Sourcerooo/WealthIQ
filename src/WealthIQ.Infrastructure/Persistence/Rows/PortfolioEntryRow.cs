namespace WealthIQ.Infrastructure.Persistence.Rows;

/// <summary>
/// One canonical ledger entry. Common columns are queryable/dedup-able;
/// the full concrete entry is preserved in <see cref="PayloadJson"/>.
/// </summary>
public sealed class PortfolioEntryRow
{
    public Guid EntryId { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string Category { get; set; } = "";

    // Idempotency key (from SourceProvenance)
    public string SourceSystem { get; set; } = "";
    public string SourceRecordReference { get; set; } = "";

    // Full concrete entry serialized as JSON
    public string PayloadJson { get; set; } = "";
}
