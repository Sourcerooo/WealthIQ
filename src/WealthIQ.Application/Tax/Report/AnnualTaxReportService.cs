using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Application.Tax.Report;

/// <summary>
/// Builds the yearly German tax report (spec §6 "Replay &amp; Compute", §9). Loads the persisted ledger,
/// enriches the instrument catalog, runs <see cref="GermanTaxCalculator"/>, and aggregates per year.
/// A missing FX/reference value surfaces as the calculator's exception (fail-fast, spec §7/§8) — callers display it.
/// </summary>
public sealed class AnnualTaxReportService(
    ILedgerStore ledgerStore,
    InstrumentCatalogBuilder catalogBuilder,
    GermanTaxCalculator calculator)
{
    private const decimal AbgeltungsteuerWithSoli = 0.26375m; // 25 % + 5.5 % Soli

    public async Task<IReadOnlyList<AnnualTaxReport>> GenerateAsync(CancellationToken ct = default)
    {
        var ledger = await ledgerStore.LoadLedgerAsync(ct);
        var catalog = catalogBuilder.Build(ledger.Instruments);
        var result = calculator.Calculate(ledger, catalog);

        return result.Entries
            .GroupBy(e => e.Year)
            .OrderBy(g => g.Key)
            .Select(BuildAnnualReport)
            .ToList();
    }

    private static AnnualTaxReport BuildAnnualReport(IGrouping<int, GermanTaxEntry> yearEntries)
    {
        var sells = yearEntries.Where(e => e.Type == GermanTaxEntryType.Sell).ToList();
        var dividends = yearEntries.Where(e => e.Type == GermanTaxEntryType.Dividend).ToList();
        var interest = yearEntries.Where(e => e.Type == GermanTaxEntryType.Interest).ToList();
        var withholding = yearEntries.Where(e => e.Type == GermanTaxEntryType.WithholdingTax).ToList();
        var vorab = yearEntries.Where(e => e.Type == GermanTaxEntryType.Vorabpauschale).ToList();

        var netSells = sells.Sum(e => e.TaxableAmount);
        var dividendTaxable = dividends.Sum(e => e.TaxableAmount);
        var interestTaxable = interest.Sum(e => e.TaxableAmount);
        var vorabTaxable = vorab.Sum(e => e.TaxableAmount);
        var foreignWithholding = withholding.Sum(e => e.ForeignWithholdingTax);

        var taxableBase = netSells + dividendTaxable + interestTaxable + vorabTaxable;
        var grossTax = Math.Max(0m, taxableBase) * AbgeltungsteuerWithSoli;
        var estimatedTax = Math.Max(0m, grossTax - foreignWithholding);

        var summary = new TaxReportSummary(netSells, dividendTaxable, interestTaxable, vorabTaxable, foreignWithholding, estimatedTax);
        return new AnnualTaxReport(yearEntries.Key, summary, sells, dividends, interest, withholding, vorab);
    }
}
