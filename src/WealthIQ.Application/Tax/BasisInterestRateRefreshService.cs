using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.Tax.Interface;

namespace WealthIQ.Application.Tax;

/// <summary>Fetches the BMF Basiszins for a year via the source; if null, returns a blocking diagnostic.
/// <see cref="SetManualAsync"/> always upserts — the manual override path (spec §20).</summary>
public sealed class BasisInterestRateRefreshService(IBasisInterestRateSource source, IBasisInterestRateStore store)
{
    public async Task<DataRefreshResult> RefreshAsync(int year, CancellationToken ct)
    {
        var record = await source.FetchAsync(year, ct);
        if (record is null)
        {
            return new DataRefreshResult(0, 0, 0,
            [
                new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.FileReadFailed,
                    $"Could not obtain Basiszins for {year} from the BMF source. Use manual override.",
                    Section: "Basiszins", SourceReference: year.ToString())
            ]);
        }

        store.Upsert(record.Year, record.Rate);
        await store.SaveChangesAsync(ct);
        return new DataRefreshResult(1, 0, 0, []);
    }

    public async Task SetManualAsync(int year, decimal rate, CancellationToken ct)
    {
        store.Upsert(year, rate);
        await store.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int year, CancellationToken ct)
    {
        store.Delete(year);
        await store.SaveChangesAsync(ct);
    }
}
