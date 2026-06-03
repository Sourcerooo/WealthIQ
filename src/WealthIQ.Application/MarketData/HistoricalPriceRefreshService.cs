using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Application.ReferenceData;

namespace WealthIQ.Application.MarketData;

/// <summary>Incremental per-symbol refresh: fetch (maxStoredDate+1 … asOf), upsert by (symbol, date).
/// A fetched bar whose currency ≠ the configured listing currency is a blocking diagnostic and the
/// symbol's bars are not written (spec §3). "Force full reload" wipes the symbol first.</summary>
public sealed class HistoricalPriceRefreshService(IHistoricalPriceProvider provider, IHistoricalPriceStore store)
{
    public async Task<DataRefreshResult> RefreshAsync(DateOnly asOf, bool forceFullReload, CancellationToken ct)
    {
        var diagnostics = new List<ImportDiagnostic>();
        int added = 0, updated = 0, skipped = 0;

        foreach (var listing in store.GetConfiguredListings())
        {
            if (forceFullReload)
            {
                store.DeleteSymbol(listing.ProviderSymbol);
            }

            var from = forceFullReload
                ? asOf.AddYears(-5)
                : (store.GetMaxStoredDate(listing.ProviderSymbol)?.AddDays(1) ?? asOf.AddYears(-5));

            if (from > asOf)
            {
                skipped++;
                continue;
            }

            HistoricalPriceFetchResult fetched;
            try
            {
                fetched = await provider.FetchAsync(listing.ProviderSymbol, from, asOf, ct);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.FileReadFailed,
                    $"Fetch failed for '{listing.ProviderSymbol}': {ex.Message}", Section: "HistoricalPrices", SourceReference: listing.ProviderSymbol));
                continue;
            }

            if (fetched.Currency != listing.Currency)
            {
                diagnostics.Add(new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.InvalidRecord,
                    $"'{listing.ProviderSymbol}' returned {fetched.Currency} but is configured as {listing.Currency}.",
                    Section: "HistoricalPrices", SourceReference: listing.ProviderSymbol));
                continue;
            }

            var (a, u) = store.Upsert(fetched.Bars);
            added += a;
            updated += u;
        }

        if (diagnostics.All(d => d.Severity < ImportDiagnosticSeverity.Error))
        {
            await store.SaveChangesAsync(ct);
        }

        return new DataRefreshResult(added, updated, skipped, diagnostics);
    }
}
