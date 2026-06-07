using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Ibkr.MarketData;
using WealthIQ.Infrastructure.Ibkr.Tax;
using WealthIQ.Infrastructure.Ibkr.Currency;
using WealthIQ.Infrastructure.ReferenceData;
using WealthIQ.Infrastructure.TradersPlace.Import;
using Xunit;

namespace WealthIQ.Tests.Application.Tax;

public sealed class TradersPlaceRegressionTests
{
    private sealed class CsvAliasMap : IDividendAliasMap
    {
        private readonly Dictionary<string, string> _map = new();

        public CsvAliasMap(string path)
        {
            foreach (var line in File.ReadLines(path).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                _map[DividendAliasNormalizer.Normalize(parts[0])] = parts[1].Trim();
            }
        }

        public string? ResolveIsin(string alias)
            => _map.TryGetValue(DividendAliasNormalizer.Normalize(alias), out var i) ? i : null;
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WealthIQ.slnx")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static async Task<ImportResult> ImportSamplesAsync()
    {
        var root = Root();
        var statements = Path.Combine(root, "data", "test", "tradersplace", "statements");
        var config = Path.Combine(root, "data", "test", "tradersplace", "configuration");
        var importer = new TradersPlaceStatementImporter(
            new CsvAliasMap(Path.Combine(config, "tradersplace_dividend_aliases.csv")));
        return await importer.ImportAsync(new ImportRequest
        {
            AccountId = (AccountId)Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Source = new ImportSource(Broker.TradersPlace, Format.CSV, statements)
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Import_BothCsvs_ParsesTradesCashAndKestUnderOneAccount()
    {
        var accountId = (AccountId)Guid.Parse("33333333-3333-3333-3333-333333333333");
        var importResult = await ImportSamplesAsync();

        Assert.DoesNotContain(importResult.Diagnostics, d => d.Severity >= ImportDiagnosticSeverity.Error);

        var trades = importResult.PortfolioLedger.Entries.OfType<TradeEntry>().ToList();
        var cash = importResult.PortfolioLedger.Entries.OfType<CashEntry>().ToList();

        Assert.Equal(15, trades.Count);  // 11 Kauf + 4 Verkauf
        Assert.Equal(6, cash.Count(c => c.CashFlowType == CashFlowType.Dividend));
        Assert.Equal(3, cash.Count(c => c.CashFlowType == CashFlowType.Interest));
        Assert.All(importResult.PortfolioLedger.Entries, e => Assert.Equal(accountId, e.AccountId));
        Assert.Equal(340.29m, trades.Where(t => t.Side == TradeSide.Sell).Sum(t => t.WithheldTax.Amount));
    }

    [Fact]
    public async Task Calculate_CleanWithinYearSales_MatchRawAndTaxableAndKest()
    {
        var root = Root();
        var config = Path.Combine(root, "data", "test", "tradersplace", "configuration");
        var importResult = await ImportSamplesAsync();

        var catalog = new InstrumentCatalogBuilder(
            new JsonInstrumentProfileEnricher(Path.Combine(config, "instruments.json")))
            .Build(importResult.Instruments);

        var priceProvider = new DerivedInstrumentPriceProvider(
            new JsonInstrumentMarketDataMap(Path.Combine(config, "listings.json")),
            new CsvHistoricalPriceLookup(Path.Combine(config, "historical_prices.csv")));

        var calculator = new GermanTaxCalculator(
            new CsvBasisInterestRateProvider(Path.Combine(config, "basiszins.csv")),
            priceProvider,
            new CsvFxRateLookup(Path.Combine(config, "fx_rates.csv")));

        var result = calculator.Calculate(importResult.PortfolioLedger, catalog);

        var sells = result.Entries.Where(e => e.Type == GermanTaxEntryType.Sell).ToList();

        // Amundi EUR Overnight (FR0010510800): bought 100+361+369 sh, sold 830 within 2024 → usedVorab 0; TFS 0 → taxable == raw.
        // Proceeds = 830 × 108.510 = 90,063.30; Cost = 100×108.259 + 361×108.259 + 369×108.278 = 89,861.98
        // Raw = 90,063.30 − 89,861.98 = 201.32
        var amundiSells = sells.Where(s => s.Isin == "FR0010510800").ToList();
        Assert.Equal(201.32m, decimal.Round(amundiSells.Sum(s => s.RawAmount), 2));
        Assert.Equal(0m, amundiSells.Sum(s => s.UsedVorabpauschale));

        // Vanguard (IE00B3XXRP09) Oct-2025 sale: 581 sh @ 112.895 from the 835 sh @ 97.888 lot (FIFO), within 2025 → usedVorab 0.
        // Proceeds = 581 × 112.895 = 65,591.995; Cost = 581 × 97.888 = 56,872.928
        // Raw = 65,591.995 − 56,872.928 = 8,719.067 → rounded 8,719.07
        // TaxableAmount = raw × 0.70 (TFS 30%) = 6,103.35
        var vanguardSells = sells.Where(s => s.Isin == "IE00B3XXRP09").ToList();
        Assert.Equal(8719.07m, decimal.Round(vanguardSells.Sum(s => s.RawAmount), 2));
        Assert.Equal(0m, vanguardSells.Sum(s => s.UsedVorabpauschale));
        Assert.Equal(6103.35m, decimal.Round(vanguardSells.Sum(s => s.TaxableAmount), 2)); // raw × 0.70
        Assert.Equal(340.29m, vanguardSells.Sum(s => s.WithheldKESt));
    }
}
