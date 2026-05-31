namespace WealthIQ.Application.Persistence.Interface;

/// <summary>
/// Copies a raw broker file into the managed audit folder (immutable source of truth, spec §6).
/// Returns the stored path, which becomes the import's <c>SourceLocation</c>.
/// </summary>
public interface IRawFileStore
{
    string Ingest(string sourceFilePath);
}
