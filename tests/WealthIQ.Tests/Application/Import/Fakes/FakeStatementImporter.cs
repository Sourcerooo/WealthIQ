using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Interface;

namespace WealthIQ.Tests.Application.Import.Fakes;

public sealed class FakeStatementImporter(ImportResult result) : IStatementImporter
{
    public string? SeenFilePath { get; private set; }

    public bool CanImport(ImportSource source) => true;

    public Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken ct)
    {
        SeenFilePath = request.Source.FilePath;
        return Task.FromResult(result);
    }
}
