namespace WealthIQ.Application.Import.Interface;

public interface IStatementImporter
{
    public bool CanImport(ImportSource source);
    public Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken ct);
}
