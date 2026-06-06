using WealthIQ.Application.Persistence.Interface;

namespace WealthIQ.Tests.Application.Import.Fakes;

public sealed class FakeRawFileStore(string ingestedPath) : IRawFileStore
{
    public string? SeenSourcePath { get; private set; }

    public string? SeenSourceDirectory { get; private set; }

    public string Ingest(string sourceFilePath)
    {
        SeenSourcePath = sourceFilePath;
        return ingestedPath;
    }

    public string IngestDirectory(string sourceDirectory)
    {
        SeenSourceDirectory = sourceDirectory;
        return ingestedPath;
    }
}
