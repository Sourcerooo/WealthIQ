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

public sealed class GermanTaxEntryDetailTests
{
    [Fact]
    public async Task Calculate_SellEntries_CarryOpenedOnAndFees()
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

        var sells = result.Entries.Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Sell).ToList();

        Assert.NotEmpty(sells);
        Assert.All(sells, s => Assert.True(s.OpenedOn != default(DateOnly), $"{s.Symbol} sell missing OpenedOn"));
        Assert.All(sells, s => Assert.True(s.OpenedOn <= s.Date, $"{s.Symbol} opened after close"));
        Assert.All(sells, s => Assert.True(s.Fees >= 0m, $"{s.Symbol} negative fees"));
    }

    [Fact]
    public async Task Calculate_WithholdingEntries_CarryOrigin()
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

        var withholdings = result.Entries
            .Where(x => x.Type == GermanTaxEntryType.WithholdingTax)
            .ToList();

        Assert.NotEmpty(withholdings);
        Assert.All(withholdings, w => Assert.False(string.IsNullOrWhiteSpace(w.Origin), "withholding missing Origin"));
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
