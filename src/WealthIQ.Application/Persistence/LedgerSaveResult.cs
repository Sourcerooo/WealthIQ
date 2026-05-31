namespace WealthIQ.Application.Persistence;

/// <summary>Outcome of an idempotent ledger save.</summary>
public sealed record LedgerSaveResult(int InsertedEntries, int SkippedDuplicateEntries);
