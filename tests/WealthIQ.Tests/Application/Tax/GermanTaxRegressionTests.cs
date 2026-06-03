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

/// <summary>
/// End-to-end regression test against TaxAlpha_Raw_Data_2021–2025.xml statements.
/// Expected values were computed after the Stage B §18-correct Vorabpauschale change that rebases
/// basisErtrag on the year-START redemption price (EarliestOnOrAfter Jan 1) instead of acquisition cost.
///
/// Key Vorabpauschale arithmetic for 2023 year-end (Basiszins = 2.55%, basisFactor = 0.01785):
///
/// IGLN.L (USD, no distributions):
///   startEur = 34.75 USD × FX(USD, 2023-01-02=0.9360666479) = 32.5283 EUR/share
///   endEur   = 40.1225 USD × FX(USD, 2023-12-29=0.9049773756) = 36.310 EUR/share
///   basisErtrag = 32.5283 × 0.01785 = 0.58063 EUR/share  (cap = 3.782, not binding)
///   vorab/share = 0.58063 × qty  →  Lot A(14sh)=8.13, Lot B(400sh)=232.25, Lot C(416sh)=241.54
///
/// VUSA.L (GBP, quarterly USD distributions≈1.021 EUR/share in 2023):
///   startEur = 57.40 GBP × FX(GBP, 2023-01-02=1.1282861334) = 64.764 EUR/share
///   endEur   = 69.9238 GBP × FX(GBP, 2023-12-29=1.1506817790) = 80.460 EUR/share
///   basisErtrag = 64.764 × 0.01785 = 1.1560 EUR/share
///   cap = (80.460 − 64.764) + 1.021 = 16.717  →  basisErtrag not binding
///   vorabFull = max(0, 1.1560 − 1.021) ≈ 0.13512 EUR/share  (distributions nearly cancel basisErtrag)
///   GBP lots (all opened pre-2023 → monthFactor=1): 1sh=0.14, 11sh=1.49, 86sh=11.62,
///       145sh=19.59, 168sh=22.70, 216sh=29.19
///
/// VUSA.AS (EUR lot, 85 shares, year-start price 56.35 EUR):
///   basisErtrag = 56.35 × 0.01785 = 1.006 EUR/share  &lt; distributions≈1.021 EUR/share
///   vorabFull = max(0, 1.006 − 1.021) = 0  →  no Vorabpauschale for this lot (distributions absorb it)
///
/// IDTL.L (USD, bonds depreciated in 2023 — start price 4.80 USD &gt; end price 3.47 USD):
///   cap = max(0, endEur − startEur) = 0  →  no Vorabpauschale
///
/// UsedVorabpauschale in 2024 sells equals accumulated vorab on each consumed lot at time of sale.
/// </summary>
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

        // IDTL: 4 FIFO consumptions from Jun 2024 sell (-29 and -2916 shares).
        //   No Vorabpauschale: bonds depreciated in 2023 (cap=0) and no 2022 vorab (negative Basiszins).
        //   TaxableAmount = RawAmount × (1 - TFS=0.00) = RawAmount.
        // IGLN: 3 FIFO consumptions from Jun 2024 sell (-830 shares).
        //   UsedVorabpauschale = 2023 accumulated vorab for each lot (TFS=0.00 → taxable=raw).
        //   Lot A(14sh): usedVorab=8.13; Lot B(400sh): usedVorab=241.54; Lot C(416sh): usedVorab=232.25.
        // VUSA: 6 FIFO consumptions from Jun 2024 sell (-392 shares):
        //   EUR lot(85sh): usedVorab=0 (basisErtrag absorbed by distributions, see class comment).
        //   GBP lots A-D: usedVorab equals each lot's 2023 Vorabpauschale accumulation.
        //   GBP lot E partial(41/216sh): usedVorab = 29.19 × (41/216) = 5.54.
        //   TaxableAmount = RawAmount × (1 − TFS=0.30) = RawAmount × 0.70.
        var expectedSellEntries = new (string Symbol, decimal RawAmount, decimal UsedVorabpauschale, decimal TaxableAmount)[]
        {
            ("IDTL", -1185.39m, 0m, -1185.39m),
            ("IDTL", -1115.67m, 0.00m, -1115.67m),
            ("IDTL", -1057.69m, 0m, -1057.69m),
            ("IDTL", -34.80m, 0m, -34.80m),
            ("IGLN", 177.19m, 8.13m, 177.19m),    // Lot A 14sh: usedVorab = 0.58063 × 14 = 8.13
            ("IGLN", 3838.59m, 241.54m, 3838.59m), // Lot C 416sh: usedVorab = 0.58063 × 416 = 241.54
            ("IGLN", 4439.52m, 232.25m, 4439.52m), // Lot B 400sh: usedVorab = 0.58063 × 400 = 232.25
            ("VUSA", 18.84m, 0.14m, 13.18m),        // GBP lot A 1sh: usedVorab=0.135×1=0.14
            ("VUSA", 283.90m, 1.49m, 198.73m),      // GBP lot B 11sh: usedVorab=0.135×11=1.49
            ("VUSA", 652.41m, 5.54m, 456.69m),      // GBP lot E partial 41sh: usedVorab=29.19×(41/216)=5.54
            ("VUSA", 2180.32m, 11.62m, 1526.23m),   // GBP lot C 86sh: usedVorab=0.135×86=11.62
            ("VUSA", 2505.94m, 0.00m, 1754.16m),    // EUR lot 85sh: usedVorab=0 (dist absorbs basisErtrag)
            ("VUSA", 2673.29m, 22.70m, 1871.30m)    // GBP lot D 168sh: usedVorab=0.135×168=22.70
        };

        Assert.Equal(
            expectedSellEntries
                .OrderBy(x => x.Symbol)
                .ThenBy(x => x.RawAmount)
                .ThenBy(x => x.UsedVorabpauschale),
            sellEntries);
        Assert.Equal(
            10882.06m,
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

        // Year=2024 Vorabpauschale = computed at 2023 year-end, posted Jan 1, 2024.
        // Basiszins 2023 = 2.55%, basisFactor = 0.01785.
        // IGLN.L (TFS=0.00 → taxable = raw):
        //   vorab/share = startEur(32.528) × 0.01785 = 0.58063  (cap = 3.782, not binding; no distributions)
        //   Lot A(14sh): 0.58063×14=8.13; Lot B(400sh): 0.58063×400=232.25; Lot C(416sh): 0.58063×416=241.54
        // VUSA.L GBP lots (TFS=0.30 → taxable = raw × 0.70):
        //   vorabFull/share = startEur(64.764)×0.01785 − distPerShare(≈1.021) ≈ 0.13512
        //   All 6 GBP lots opened pre-2023 → monthFactor=1
        //   1sh=0.14(tax=0.09), 11sh=1.49(1.04), 86sh=11.62(8.13),
        //   145sh=19.59(13.71), 168sh=22.70(15.89), 216sh=29.19(20.43)
        // VUSA.AS EUR lot: vorabFull=0 (basisErtrag < distributions) — no entry.
        // IDTL: no entry (bonds depreciated, cap=0).
        var expectedVorabEntries = new (string Symbol, decimal RawAmount, decimal TaxableAmount)[]
        {
            // IGLN lots — TFS=0.00, taxable=raw
            ("IGLN", 8.13m, 8.13m),      // Lot A: 0.58063 × 14 sh
            ("IGLN", 232.25m, 232.25m),  // Lot B: 0.58063 × 400 sh
            ("IGLN", 241.54m, 241.54m),  // Lot C: 0.58063 × 416 sh
            // VUSA GBP lots — TFS=0.30, taxable=raw×0.70; vorabFull/sh≈0.13512
            ("VUSA", 0.14m, 0.09m),      // GBP lot A: 0.13512 × 1 sh
            ("VUSA", 1.49m, 1.04m),      // GBP lot B: 0.13512 × 11 sh
            ("VUSA", 11.62m, 8.13m),     // GBP lot C: 0.13512 × 86 sh
            ("VUSA", 19.59m, 13.71m),    // GBP lot F: 0.13512 × 145 sh (Feb 2022 lot)
            ("VUSA", 22.70m, 15.89m),    // GBP lot D: 0.13512 × 168 sh
            ("VUSA", 29.19m, 20.43m),    // GBP lot E: 0.13512 × 216 sh
        };

        Assert.Equal(
            expectedVorabEntries
                .OrderBy(x => x.Symbol)
                .ThenBy(x => x.RawAmount),
            vorabEntries);
        Assert.Equal(
            541.23m,
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
