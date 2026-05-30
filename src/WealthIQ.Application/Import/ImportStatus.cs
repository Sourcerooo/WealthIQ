namespace WealthIQ.Application.Import;

/// <summary>Outcome of an import batch: committed to the DB, or aborted before any write.</summary>
public enum ImportStatus
{
    Committed,
    Aborted
}
