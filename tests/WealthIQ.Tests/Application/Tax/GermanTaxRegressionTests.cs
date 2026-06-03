using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Tax;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Ibkr.Currency;
using WealthIQ.Infrastructure.Ibkr.Import;
using WealthIQ.Infrastructure.Ibkr.MarketData;
using WealthIQ.Infrastructure.Ibkr.Tax;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Application.Tax;

public sealed class GermanTaxRegressionTests
{
    [Fact]
    public async Task Calculate_2024SampleData_MatchesSigmaticDisposalsAndVorabpauschale()
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

        var sellEntries = result.Entries
            .Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Sell)
            .Select(x => (
                Symbol: x.Symbol,
                RawAmount: decimal.Round(x.RawAmount, 2),
                UsedVorabpauschale: decimal.Round(x.UsedVorabpauschale, 2),
                TaxableAmount: decimal.Round(x.TaxableAmount, 2)))
            .OrderBy(x => x.Symbol)
            .ThenBy(x => x.RawAmount)
            .ThenBy(x => x.UsedVorabpauschale)
            .ToList();

        var expectedSellEntries = new (string Symbol, decimal RawAmount, decimal UsedVorabpauschale, decimal TaxableAmount)[]
        {
            ("IDTL", -1185.39m, 0m, -1185.39m),
            ("IDTL", -1115.67m, 0m, -1115.67m),
            ("IDTL", -1057.69m, 0m, -1057.69m),
            ("IDTL", -34.80m, 0m, -34.80m),
            ("IGLN", 178.05m, 7.27m, 178.05m),
            ("IGLN", 3838.63m, 241.50m, 3838.63m),
            ("IGLN", 4452.93m, 218.85m, 4452.93m),
            ("VUSA", 18.64m, 0.33m, 13.05m),
            ("VUSA", 283.09m, 2.30m, 198.16m),
            ("VUSA", 642.12m, 15.83m, 449.48m),
            ("VUSA", 2173.23m, 18.71m, 1521.26m),
            ("VUSA", 2493.51m, 12.44m, 1745.45m),
            ("VUSA", 2631.13m, 64.86m, 1841.79m)
        };

        Assert.Equal(
            expectedSellEntries
                .OrderBy(x => x.Symbol)
                .ThenBy(x => x.RawAmount)
                .ThenBy(x => x.UsedVorabpauschale),
            sellEntries);
        Assert.Equal(
            10845.26m,
            decimal.Round(result.Entries.Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Sell).Sum(x => x.TaxableAmount), 2));

        var vorabEntries = result.Entries
            .Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Vorabpauschale)
            .Select(x => (
                Symbol: x.Symbol,
                RawAmount: decimal.Round(x.RawAmount, 2),
                TaxableAmount: decimal.Round(x.TaxableAmount, 2)))
            .OrderBy(x => x.Symbol)
            .ThenBy(x => x.RawAmount)
            .ToList();

        var expectedVorabEntries = new (string Symbol, decimal RawAmount, decimal TaxableAmount)[]
        {
            ("VUSA", 0.33m, 0.23m),
            ("VUSA", 2.30m, 1.61m),
            ("VUSA", 12.44m, 8.70m),
            ("VUSA", 18.71m, 13.10m),
            ("VUSA", 83.40m, 58.38m),
            ("VUSA", 64.86m, 45.40m),
            ("VUSA", 43.12m, 30.18m),
            ("IGLN", 7.27m, 7.27m),
            ("IGLN", 218.85m, 218.85m),
            ("IGLN", 241.50m, 241.50m)
        };

        Assert.Equal(
            expectedVorabEntries
                .OrderBy(x => x.Symbol)
                .ThenBy(x => x.RawAmount),
            vorabEntries);
        Assert.Equal(
            625.24m,
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
