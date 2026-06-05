using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Application.ReferenceData;

namespace WealthIQ.Application.MarketData;

/// <summary>Refreshes stored historical bars from the provider, upserting by (symbol, date).
/// A fetched bar whose currency ≠ the configured listing currency is a blocking diagnostic and that
/// symbol's bars are not written (spec §3). Incremental mode fetches (maxStoredDate+1 … asOf) per symbol;
/// range mode fetches an explicit [from, to] for the selected symbols. "Force full reload" wipes first.</summary>
public sealed class HistoricalPriceRefreshService(IHistoricalPriceProvider provider, IHistoricalPriceStore store)
{
    /// <summary>Incremental refresh of all configured listings (maxStoredDate+1 … asOf).</summary>
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

            await FetchOneAsync(listing, from, asOf, diagnostics, (a, u) => { added += a; updated += u; }, ct);
        }

        if (diagnostics.All(d => d.Severity < ImportDiagnosticSeverity.Error))
        {
            await store.SaveChangesAsync(ct);
        }

        return new DataRefreshResult(added, updated, skipped, diagnostics);
    }

    /// <summary>Targeted refresh: fetches an explicit [from, to] range for the given provider symbols.
    /// An empty/null symbol list refreshes all configured listings. When <paramref name="forceFullReload"/>
    /// is true, each selected symbol is wiped before refetching.</summary>
    public async Task<DataRefreshResult> RefreshRangeAsync(
        IReadOnlyList<string>? providerSymbols, DateOnly from, DateOnly to, bool forceFullReload, CancellationToken ct)
    {
        var diagnostics = new List<ImportDiagnostic>();
        int added = 0, updated = 0, skipped = 0;

        var wanted = providerSymbols is { Count: > 0 }
            ? new HashSet<string>(providerSymbols, StringComparer.OrdinalIgnoreCase)
            : null;

        var listings = store.GetConfiguredListings()
            .Where(l => wanted is null || wanted.Contains(l.ProviderSymbol))
            .ToList();

        if (listings.Count == 0)
        {
            diagnostics.Add(new ImportDiagnostic(ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.InvalidRecord,
                "No matching configured listings to refresh.", Section: "HistoricalPrices"));
        }

        foreach (var listing in listings)
        {
            if (forceFullReload)
            {
                store.DeleteSymbol(listing.ProviderSymbol);
            }

            if (from > to)
            {
                skipped++;
                continue;
            }

            await FetchOneAsync(listing, from, to, diagnostics, (a, u) => { added += a; updated += u; }, ct);
        }

        if (diagnostics.All(d => d.Severity < ImportDiagnosticSeverity.Error))
        {
            await store.SaveChangesAsync(ct);
        }

        return new DataRefreshResult(added, updated, skipped, diagnostics);
    }

    private async Task FetchOneAsync(
        HistoricalPriceSymbol listing, DateOnly from, DateOnly to,
        List<ImportDiagnostic> diagnostics, Action<int, int> accumulate, CancellationToken ct)
    {
        HistoricalPriceFetchResult fetched;
        try
        {
            fetched = await provider.FetchAsync(listing.ProviderSymbol, from, to, ct);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.FileReadFailed,
                $"Fetch failed for '{listing.ProviderSymbol}': {ex.Message}", Section: "HistoricalPrices", SourceReference: listing.ProviderSymbol));
            return;
        }

        if (fetched.Currency != listing.Currency)
        {
            diagnostics.Add(new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.InvalidRecord,
                $"'{listing.ProviderSymbol}' returned {fetched.Currency} but is configured as {listing.Currency}.",
                Section: "HistoricalPrices", SourceReference: listing.ProviderSymbol));
            return;
        }

        var (a, u) = store.Upsert(fetched.Bars);
        accumulate(a, u);
    }
}
