using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Tax;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.IBKR.Import;
using WealthIQ.Infrastructure.IBKR.Tax;

namespace WealthIQ.Tests.Application.Tax;

public sealed class GermanTaxRegressionTests
{
    [Fact(Skip = "Pending dedicated FX conversion layer for source-currency ledger replay.")]
    public async Task Calculate_2024SampleData_MatchesSigmaticDisposalsAndVorabpauschale()
    {
        var repoRoot = FindRepositoryRoot();
        var inputPath = Path.Combine(repoRoot, "data", "old_project", "Frontend", "ConsoleUi", "Sigmatic.Console", "Input");
        var configurationPath = Path.Combine(repoRoot, "data", "old_project", "Frontend", "ConsoleUi", "Sigmatic.Console", "Input", "Configuration");

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

        var calculator = new GermanTaxCalculator(
            new CsvBasisInterestRateProvider(Path.Combine(configurationPath, "basiszins.csv")),
            new CsvYearEndPriceProvider(Path.Combine(configurationPath, "prices.csv")));

        var result = calculator.Calculate(importResult.PortfolioLedger, instrumentCatalog);

        var sellEntries = result.Entries
            .Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Sell)
            .Select(x => (
                Symbol: x.Symbol,
                RawAmount: decimal.Round(x.RawAmount, 2),
                UsedVorabpauschale: decimal.Round(x.UsedVorabpauschale, 2),
                TaxableAmount: decimal.Round(x.TaxableAmount, 2)))
            .ToList();

        var expectedSellEntries = new[]
        {
            ("VUSA", 2483.52m, 12.40m, 1738.46m),
            ("VUSA", 18.58m, 0.33m, 13.00m),
            ("VUSA", 282.36m, 2.29m, 197.65m),
            ("VUSA", 2148.76m, 18.92m, 1504.13m),
            ("VUSA", 2572.94m, 54.61m, 1801.05m),
            ("VUSA", 627.92m, 13.33m, 439.54m),
            ("IGLN", 176.38m, 7.29m, 176.38m),
            ("IGLN", 4419.31m, 219.20m, 4419.31m),
            ("IGLN", 3814.38m, 241.68m, 3814.38m),
            ("IDTL", -34.58m, 0m, -34.58m),
            ("IDTL", -1177.52m, 0m, -1177.52m),
            ("IDTL", -1050.66m, 0m, -1050.66m),
            ("IDTL", -1115.36m, 0m, -1115.36m)
        };

        Assert.Equal(expectedSellEntries, sellEntries);
        Assert.Equal(
            10725.80m,
            decimal.Round(result.Entries.Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Sell).Sum(x => x.TaxableAmount), 2));

        var vorabEntries = result.Entries
            .Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Vorabpauschale)
            .Select(x => (
                Symbol: x.Symbol,
                RawAmount: decimal.Round(x.RawAmount, 2),
                TaxableAmount: decimal.Round(x.TaxableAmount, 2)))
            .ToList();

        var expectedVorabEntries = new[]
        {
            ("VUSA", 12.40m, 8.68m),
            ("VUSA", 0.33m, 0.23m),
            ("VUSA", 2.29m, 1.60m),
            ("VUSA", 18.92m, 13.25m),
            ("VUSA", 54.61m, 38.23m),
            ("VUSA", 70.21m, 49.15m),
            ("VUSA", 43.13m, 30.19m),
            ("IGLN", 7.29m, 7.29m),
            ("IGLN", 219.20m, 219.20m),
            ("IGLN", 241.68m, 241.68m)
        };

        Assert.Equal(expectedVorabEntries, vorabEntries);
        Assert.Equal(
            609.49m,
            decimal.Round(result.Entries.Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Vorabpauschale).Sum(x => x.TaxableAmount), 2));
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
