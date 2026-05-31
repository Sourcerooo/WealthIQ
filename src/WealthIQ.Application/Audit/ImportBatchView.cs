namespace WealthIQ.Application.Audit;

/// <summary>One persisted import run, for the Audit page.</summary>
public sealed record ImportBatchView(
    Guid BatchId,
    string Broker,
    string Format,
    Guid AccountId,
    string RawFilePath,
    DateTimeOffset ImportedAt,
    int InsertedEntries,
    int SkippedDuplicateEntries,
    string Status);
