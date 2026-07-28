using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Tests.Application.Tax;

/// <summary>
/// End-to-end regression test against TaxAlpha_Raw_Data_2021–2025.xml statements.
/// Expected values were computed after the Stage B §18-correct Vorabpauschale change that rebases
/// basisErtrag on the year-START redemption price (EarliestOnOrAfter Jan 1) instead of acquisition cost.
///
/// Key Vorabpauschale arithmetic for 2023 year-end (Basiszins = 2.55%, basisFactor = 0.01785):
///
/// IGLN.L is an ETC (a secured debt security, not an investment fund): the InvStG does not apply,
/// so it carries no Vorabpauschale at all. Its 2024 sells therefore have UsedVorabpauschale = 0 and
/// RawAmount is the plain FIFO gain. See docs/superpowers/specs/2026-07-28-tax-form-line-mapping-design.md §6.3.
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
        var (importResult, result) = await TaxFixture.CalculateAsync();

        Assert.DoesNotContain(importResult.Diagnostics, x => x.Severity >= WealthIQ.Application.Import.Diagnostic.ImportDiagnosticSeverity.Error);

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
        //   No Vorabpauschale — ETC, outside the InvStG (TFS=0.00 → taxable=raw).
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
            ("IGLN", 185.32m, 0m, 185.32m),      // Lot A 14sh: no Vorabpauschale — ETC, outside the InvStG
            ("IGLN", 4080.14m, 0m, 4080.14m),    // Lot C 416sh: no Vorabpauschale — ETC, outside the InvStG
            ("IGLN", 4671.78m, 0m, 4671.78m),    // Lot B 400sh: no Vorabpauschale — ETC, outside the InvStG
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
            11363.98m,
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
        // IGLN.L: no entry — ETC, outside the InvStG (see class comment).
        // VUSA.L GBP lots (TFS=0.30 → taxable = raw × 0.70):
        //   vorabFull/share = startEur(64.764)×0.01785 − distPerShare(≈1.021) ≈ 0.13512
        //   All 6 GBP lots opened pre-2023 → monthFactor=1
        //   1sh=0.14(tax=0.09), 11sh=1.49(1.04), 86sh=11.62(8.13),
        //   145sh=19.59(13.71), 168sh=22.70(15.89), 216sh=29.19(20.43)
        // VUSA.AS EUR lot: vorabFull=0 (basisErtrag < distributions) — no entry.
        // IDTL: no entry (bonds depreciated, cap=0).
        var expectedVorabEntries = new (string Symbol, decimal RawAmount, decimal TaxableAmount)[]
        {
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
            59.30m,
            decimal.Round(result.Entries.Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Vorabpauschale).Sum(x => x.TaxableAmount), 2));
    }
}
