using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Application.Tax.Report;

/// <summary>
/// Builds the yearly German tax report (spec §6, §9), grouped per account (spec §8). Loads the
/// persisted ledger, enriches the instrument catalog, runs <see cref="GermanTaxCalculator"/>, then
/// groups by (AccountId, Year). A missing FX/reference value surfaces as the calculator's exception.
/// </summary>
public sealed class AnnualTaxReportService(
    ILedgerStore ledgerStore,
    InstrumentCatalogBuilder catalogBuilder,
    GermanTaxCalculator calculator)
{
    private const decimal AbgeltungsteuerWithSoli = 0.26375m; // 25 % + 5.5 % Soli

    public async Task<IReadOnlyList<AccountTaxReport>> GenerateAsync(CancellationToken ct = default)
    {
        var ledger = await ledgerStore.LoadLedgerAsync(ct);
        var catalog = catalogBuilder.Build(ledger.Instruments);
        var result = calculator.Calculate(ledger, catalog);

        var accountNumbers = ledger.Accounts.ToDictionary(a => a.AccountId, a => a.AccountNumber);

        return result.Entries
            .GroupBy(e => e.AccountId)
            .OrderBy(g => accountNumbers.TryGetValue(g.Key, out var n) ? n : g.Key.ToString(), StringComparer.Ordinal)
            .Select(accountGroup => new AccountTaxReport(
                accountGroup.Key.Value,
                accountNumbers.TryGetValue(accountGroup.Key, out var number) ? number : accountGroup.Key.ToString(),
                accountGroup
                    .GroupBy(e => e.Year)
                    .OrderBy(y => y.Key)
                    .Select(BuildAnnualReport)
                    .ToList()))
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
        var withheldKest = sells.Sum(e => e.WithheldKESt);

        var taxableBase = netSells + dividendTaxable + interestTaxable + vorabTaxable;
        var grossTax = Math.Max(0m, taxableBase) * AbgeltungsteuerWithSoli;
        var estimatedTax = Math.Max(0m, grossTax - foreignWithholding);

        var summary = new TaxReportSummary(
            netSells, dividendTaxable, interestTaxable, vorabTaxable, foreignWithholding, estimatedTax, withheldKest);
        return new AnnualTaxReport(yearEntries.Key, summary, sells, dividends, interest, withholding, vorab);
    }
}
