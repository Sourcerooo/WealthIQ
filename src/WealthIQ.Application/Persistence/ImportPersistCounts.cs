namespace WealthIQ.Application.Persistence;

/// <summary>Counts returned by a committed import persist.</summary>
public sealed record ImportPersistCounts(int InsertedEntries, int SkippedDuplicateEntries, int PersistedDiagnostics);
