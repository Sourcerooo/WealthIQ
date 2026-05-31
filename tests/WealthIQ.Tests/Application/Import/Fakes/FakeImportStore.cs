using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Persistence;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Tests.Application.Import.Fakes;

public sealed class FakeImportStore(ImportPersistCounts counts) : IImportStore
{
    public int CallCount { get; private set; }
    public ImportBatch? SeenBatch { get; private set; }
    public PortfolioLedger? SeenLedger { get; private set; }
    public IReadOnlyList<ImportDiagnostic>? SeenDiagnostics { get; private set; }

    public int FailedCallCount { get; private set; }
    public ImportBatch? SeenFailedBatch { get; private set; }
    public IReadOnlyList<ImportDiagnostic>? SeenFailedDiagnostics { get; private set; }

    public Task<ImportPersistCounts> PersistImportAsync(
        ImportBatch batch, PortfolioLedger ledger, IReadOnlyList<ImportDiagnostic> diagnostics, CancellationToken ct = default)
    {
        CallCount++;
        SeenBatch = batch;
        SeenLedger = ledger;
        SeenDiagnostics = diagnostics;
        return Task.FromResult(counts);
    }

    public Task PersistFailedImportAsync(
        ImportBatch batch,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        CancellationToken ct = default)
    {
        FailedCallCount++;
        SeenFailedBatch = batch;
        SeenFailedDiagnostics = diagnostics;
        return Task.CompletedTask;
    }
}
