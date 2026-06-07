using WealthIQ.Application.Persistence.Interface;

namespace WealthIQ.Infrastructure.Ingest;

/// <summary>
/// Stores raw broker files under a root audit folder. Re-ingesting the same file name overwrites
/// (the bytes are the immutable source; a re-import of the same statement is harmless).
/// </summary>
public sealed class FileSystemRawFileStore(string rootFolder) : IRawFileStore
{
    public string Ingest(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Raw statement file not found.", sourceFilePath);
        }

        Directory.CreateDirectory(rootFolder);
        var destination = Path.Combine(rootFolder, Path.GetFileName(sourceFilePath));
        File.Copy(sourceFilePath, destination, overwrite: true);
        return destination;
    }

    public string IngestDirectory(string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Raw statement directory not found: {sourceDirectory}");
        }

        Directory.CreateDirectory(rootFolder);
        var subfolder = Path.Combine(rootFolder, $"import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(subfolder);
        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(subfolder, Path.GetFileName(file)), overwrite: true);
        }

        return subfolder;
    }
}
