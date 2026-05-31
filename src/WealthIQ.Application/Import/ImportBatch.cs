using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Application.Import;

public enum ImportBatchStatus
{
    Committed,
    Failed
}

/// <summary>One persisted import run. Failed batches carry diagnostics but no ledger entries.</summary>
public sealed record ImportBatch(
    Guid BatchId,
    Broker Broker,
    Format Format,
    AccountId AccountId,
    string RawFilePath,
    DateTimeOffset ImportedAt,
    ImportBatchStatus Status = ImportBatchStatus.Committed);
