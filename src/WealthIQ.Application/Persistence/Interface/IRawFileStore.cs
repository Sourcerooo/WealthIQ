namespace WealthIQ.Application.Persistence.Interface;

/// <summary>
/// Copies a raw broker file into the managed audit folder (immutable source of truth, spec §6).
/// Returns the stored path, which becomes the import's <c>SourceLocation</c>.
/// </summary>
public interface IRawFileStore
{
    string Ingest(string sourceFilePath);

    /// <summary>Copies every file from a source directory into an isolated audit subfolder and returns
    /// that subfolder. Used for multi-file imports (e.g. Trader's Place: trades + cash CSVs together).</summary>
    string IngestDirectory(string sourceDirectory);
}
