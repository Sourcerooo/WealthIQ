using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Report;
using WealthIQ.Application.Tax.Report.Forms;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Ibkr.Currency;
using WealthIQ.Infrastructure.Ibkr.Import;
using WealthIQ.Infrastructure.Ibkr.MarketData;
using WealthIQ.Infrastructure.Ibkr.Tax;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Application.Tax.Forms;

/// <summary>
/// End-to-end regression test proving that <see cref="TaxFormReportBuilder"/> keeps fund income on
/// Anlage KAP-INV and non-fund income (in particular the IGLN gold ETC) on Anlage KAP, using the same
/// real IBKR fixture as <see cref="GermanTaxRegressionTests"/>. Expected figures are the Task 4 baseline
/// (see docs/superpowers/specs/2026-07-28-tax-form-line-mapping-design.md §6.3).
/// </summary>
public sealed class TaxFormReportGoldenTests
{
    [Fact]
    public async Task Build_2024Fixture_KeepsFundIncomeOnKapInvAndTheGoldEtcOnKap()
    {
        var repoRoot = FindRepositoryRoot();
        var inputPath = Path.Combine(repoRoot, "data", "test", "statements");
        var configurationPath = Path.Combine(repoRoot, "data", "test", "configuration");

        var importer = new IbkrStatementImporter();
        var importResult = await importer.ImportAsync(new ImportRequest
        {
            AccountId = (AccountId)Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, inputPath)
        }, CancellationToken.None);

        Assert.DoesNotContain(importResult.Diagnostics, x => x.Severity >= WealthIQ.Application.Import.Diagnostic.ImportDiagnosticSeverity.Error);

        var instrumentCatalog = new InstrumentCatalogBuilder(
            new JsonInstrumentProfileEnricher(Path.Combine(configurationPath, "instruments.json")))
            .Build(importResult.Instruments);

        var priceProvider = new DerivedInstrumentPriceProvider(
            new JsonInstrumentMarketDataMap(Path.Combine(configurationPath, "listings.json")),
            new CsvHistoricalPriceLookup(Path.Combine(configurationPath, "historical_prices.csv")));

        var calculator = new GermanTaxCalculator(
            new CsvBasisInterestRateProvider(Path.Combine(configurationPath, "basiszins.csv")),
            priceProvider,
            new CsvFxRateLookup(Path.Combine(configurationPath, "fx_rates.csv")));

        var result = calculator.Calculate(importResult.PortfolioLedger, instrumentCatalog);

        var entries = result.Entries.Where(x => x.Year == 2024).ToList();
        var annual = new AnnualTaxReport(
            2024,
            new TaxReportSummary(0m, 0m, 0m, 0m, 0m, 0m, 0m),
            entries.Where(x => x.Type == GermanTaxEntryType.Sell).ToList(),
            entries.Where(x => x.Type == GermanTaxEntryType.Dividend).ToList(),
            entries.Where(x => x.Type == GermanTaxEntryType.Interest).ToList(),
            entries.Where(x => x.Type == GermanTaxEntryType.WithholdingTax).ToList(),
            entries.Where(x => x.Type == GermanTaxEntryType.Vorabpauschale).ToList());

        var form = TaxFormReportBuilder.Build(annual);

        decimal Amount(string formName, string line) => form.Sections
            .Where(s => s.Form == formName).SelectMany(s => s.Lines).Single(x => x.Line == line).Amount;

        static decimal R(decimal value) => decimal.Round(value, 2);

        // VUSA is ETF_EQUITY -> Aktienfonds. Baseline is 8314.70 (sum of rounded per-lot values);
        // here we round the unrounded sum, so a 1-cent difference is expected arithmetic noise
        // (see task-7 brief).
        Assert.Equal(8314.71m, R(Amount("KAP-INV", "14")));
        // Baseline is 84.73 (sum of rounded per-lot values); a 1-cent difference from rounding the
        // unrounded sum is expected noise.
        Assert.Equal(84.72m, R(Amount("KAP-INV", "9")));

        // IDTL is ETF_BOND -> sonstiger Investmentfonds; 2024 was a loss year and it had no
        // Vorabpauschale (the bonds depreciated in 2023, so the cap was 0).
        Assert.Equal(-3393.55m, R(Amount("KAP-INV", "26")));
        Assert.Equal(0m, R(Amount("KAP-INV", "13")));

        // IGLN is an ETC, not an investment fund: its 8937.22 gain must appear on Anlage KAP
        // Zeile 19 and on no KAP-INV sale line at all.
        // Baseline is 4921.15 (8314.70 - 3393.55); consistent with the 1-cent VUSA rounding noise
        // above (8314.71 - 3393.55 = 4921.16).
        var kapInvSales = KapInvRows.All.Sum(row => Amount("KAP-INV", row.SaleLine));
        Assert.Equal(4921.16m, R(kapInvSales));

        // Baseline is 8937.22 (sum of rounded per-lot IGLN sells); a 1-cent difference from rounding
        // the unrounded sum is expected noise.
        var interest = annual.Interest.Sum(x => x.RawAmount);
        Assert.Equal(8937.23m, R(Amount("KAP", "19") - interest));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WealthIQ.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located.");
    }
}
