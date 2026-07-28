using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Tax;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Tax;
using WealthIQ.Infrastructure.Ibkr.Currency;
using WealthIQ.Infrastructure.Ibkr.Import;
using WealthIQ.Infrastructure.Ibkr.MarketData;
using WealthIQ.Infrastructure.Ibkr.Tax;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Application.Tax;

/// <summary>Shared golden-fixture wiring for tests that run the real IBKR statements
/// (data/test/statements + data/test/configuration) through the full import → catalog →
/// price/FX providers → <see cref="GermanTaxCalculator"/> pipeline. Used by
/// <see cref="GermanTaxRegressionTests"/>, <see cref="GermanTaxEntryDetailTests"/>, and
/// <see cref="WealthIQ.Tests.Application.Tax.Forms.TaxFormReportGoldenTests"/> so the pipeline
/// wiring lives in exactly one place and cannot drift between copies.</summary>
internal static class TaxFixture
{
    /// <summary>Walks up from the test binary's output directory to find the repo root
    /// (identified by <c>WealthIQ.slnx</c>), so fixture paths resolve regardless of the
    /// build configuration or working directory the test runner uses.</summary>
    internal static string RepositoryRoot()
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

    /// <summary>Imports the golden IBKR fixture and runs it through <see cref="GermanTaxCalculator"/>.
    /// Returns both the import result (for diagnostics assertions) and the calculation result
    /// (for the produced <see cref="GermanTaxEntry"/> entries).</summary>
    internal static async Task<(ImportResult ImportResult, GermanTaxCalculationResult Result)> CalculateAsync()
    {
        var repoRoot = RepositoryRoot();
        var inputPath = Path.Combine(repoRoot, "data", "test", "statements");
        var configurationPath = Path.Combine(repoRoot, "data", "test", "configuration");

        var importer = new IbkrStatementImporter();
        var importResult = await importer.ImportAsync(new ImportRequest
        {
            AccountId = (AccountId)Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, inputPath)
        }, CancellationToken.None);

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

        return (importResult, result);
    }
}
