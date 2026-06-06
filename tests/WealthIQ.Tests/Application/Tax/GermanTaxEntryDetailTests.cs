using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Tax;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Tax;
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
        var entries = await BuildEntriesAsync();

        var sells = entries.Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Sell).ToList();

        Assert.NotEmpty(sells);
        Assert.All(sells, s => Assert.True(s.OpenedOn != default(DateOnly), $"{s.Symbol} sell missing OpenedOn"));
        Assert.All(sells, s => Assert.True(s.OpenedOn <= s.Date, $"{s.Symbol} opened after close"));
        Assert.All(sells, s => Assert.True(s.Fees >= 0m, $"{s.Symbol} negative fees"));
    }

    [Fact]
    public async Task Calculate_WithholdingEntries_CarryOrigin()
    {
        var entries = await BuildEntriesAsync();

        var withholdings = entries
            .Where(x => x.Type == GermanTaxEntryType.WithholdingTax)
            .ToList();

        Assert.NotEmpty(withholdings);
        Assert.All(withholdings, w => Assert.False(string.IsNullOrWhiteSpace(w.Origin), "withholding missing Origin"));
    }

    [Fact]
    public async Task Calculate_DividendEntries_CarrySourceReferenceAndOriginalAmount()
    {
        var entries = await BuildEntriesAsync();
        var dividends = entries.Where(x => x.Type == GermanTaxEntryType.Dividend).ToList();

        Assert.NotEmpty(dividends);
        Assert.All(dividends, d => Assert.False(string.IsNullOrWhiteSpace(d.SourceReference), $"{d.Symbol} dividend missing SourceReference"));
        Assert.All(dividends, d => Assert.False(string.IsNullOrWhiteSpace(d.OriginalCurrency), $"{d.Symbol} dividend missing OriginalCurrency"));
    }

    [Fact]
    public async Task Calculate_SellEntries_CarryOpenAndCloseReferences()
    {
        var entries = await BuildEntriesAsync();
        var sells = entries.Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Sell).ToList();

        Assert.NotEmpty(sells);
        Assert.All(sells, s => Assert.False(string.IsNullOrWhiteSpace(s.SourceReference), $"{s.Symbol} sell missing open ref"));
        Assert.All(sells, s => Assert.False(string.IsNullOrWhiteSpace(s.CloseReference), $"{s.Symbol} sell missing close ref"));
    }

    [Fact]
    public async Task Calculate_VorabpauschaleEntries_CarryCalculationInputs()
    {
        var entries = await BuildEntriesAsync();
        var vorab = entries.Where(x => x.Type == GermanTaxEntryType.Vorabpauschale).ToList();

        Assert.NotEmpty(vorab);
        Assert.All(vorab, v => Assert.True(v.YearStartPrice > 0m, $"{v.Symbol} vorab missing YearStartPrice"));
        Assert.All(vorab, v => Assert.True(v.BasisRate > 0m, $"{v.Symbol} vorab missing BasisRate"));
        Assert.All(vorab, v => Assert.True(v.HeldQuantity > 0m, $"{v.Symbol} vorab missing HeldQuantity"));
    }

    private static async Task<IReadOnlyList<GermanTaxEntry>> BuildEntriesAsync()
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

        return calculator.Calculate(importResult.PortfolioLedger, instrumentCatalog).Entries;
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
