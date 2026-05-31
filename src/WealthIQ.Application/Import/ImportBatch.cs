using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Application.Import;

/// <summary>One persisted import run. Stored only when the batch commits.</summary>
public sealed record ImportBatch(
    Guid BatchId,
    Broker Broker,
    Format Format,
    AccountId AccountId,
    string RawFilePath,
    DateTimeOffset ImportedAt);
