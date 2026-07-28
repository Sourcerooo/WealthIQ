using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Application.Tax;

public sealed class GermanTaxCalculatorTests
{
    [Fact]
    public void Calculate_BuyDividendVorabAndSell_ProducesExpectedTaxEntries()
    {
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instruments = new[]
        {
            new Instrument(instrumentId, "IE00B6R52259", "ACWI", "ACWI", 0.30m) { SubjectToVorabpauschale = true }
        };

        // year-start price = 100 (= acquisition price); year-end = 120
        // With new §18 algorithm: basisErtrag = 100×0.025×0.7 = 1.75; dist/sh = 5/10 = 0.50;
        // cap = (120-100)+0.50 = 20.50; capped = 1.75; vorabFull = 1.75-0.50 = 1.25; ×10 = 12.5
        var calculator = new GermanTaxCalculator(
            new StubInterestRateProvider((2024, 0.025m)),
            new StubYearStartAndEndPriceProvider(("IE00B6R52259", 2024, 100m, 120m)),
            new StubFxRateLookup());

        var result = calculator.Calculate(new PortfolioLedger([
            new TradeEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero),
                new DateOnly(2024, 1, 15),
                TaxCalculatorTestDoubles.SourceProvenance("BUY-1"),
                instrumentId,
                TradeSide.Buy,
                new Quantity(10m),
                new Money(100m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR)),
            new CashEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero),
                new DateOnly(2024, 6, 10),
                TaxCalculatorTestDoubles.SourceProvenance("DIV-1"),
                InstrumentId.NewId(),
                CashFlowType.Dividend,
                new Money(5m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                instrumentId),
            new TradeEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2025, 2, 1, 9, 0, 0, TimeSpan.Zero),
                new DateOnly(2025, 2, 1),
                TaxCalculatorTestDoubles.SourceProvenance("SELL-1"),
                instrumentId,
                TradeSide.Sell,
                new Quantity(10m),
                new Money(130m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR))
        ]), instruments);

        Assert.Equal(3, result.Entries.Count);

        var dividendEntry = result.Entries.Single(x => x.Type == GermanTaxEntryType.Dividend);
        Assert.Equal(5m, dividendEntry.RawAmount);
        Assert.Equal(3.5m, dividendEntry.TaxableAmount);

        var vorabEntry = result.Entries.Single(x => x.Type == GermanTaxEntryType.Vorabpauschale);
        Assert.Equal(new DateOnly(2025, 1, 1), vorabEntry.Date);
        Assert.Equal(12.5m, vorabEntry.RawAmount);
        Assert.Equal(8.75m, vorabEntry.TaxableAmount);

        var sellEntry = result.Entries.Single(x => x.Type == GermanTaxEntryType.Sell);
        Assert.Equal(287.5m, sellEntry.RawAmount);
        Assert.Equal(201.25m, sellEntry.TaxableAmount);
        Assert.Equal(12.5m, sellEntry.UsedVorabpauschale);
        Assert.Equal(10m, sellEntry.QuantitySold);

        Assert.All(result.OpenLots, lot => Assert.Equal(0m, lot.RemainingQuantity.Value));
    }

    [Fact]
    public void Calculate_SellAtLossAfterVorabpauschale_DeductionEnlargesTheLoss()
    {
        // Scenario (§19 InvStG): a fund gained in 2024, so a Vorabpauschale was taxed (posted 2025-01-01).
        // In 2025 the fund falls and is sold at an overall loss. The previously-taxed Vorabpauschale is
        // deducted from the sale proceeds, which makes the realized LOSS larger — it does not vanish.
        // (Economically the prepaid Vorab is recovered via that enlarged loss offsetting other capital
        // income; the loss-offset pots / multi-year carry-forward are not modelled here — see CLAUDE.md.)
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instruments = new[]
        {
            new Instrument(instrumentId, "IE00B6R52259", "ACWI", "ACWI", 0.30m) { SubjectToVorabpauschale = true }
        };

        // 2024 year-start = 100 (= buy price), year-end = 120 → appreciation, so a Vorabpauschale is posted.
        // basisErtrag/share = 100 × 0.025 × 0.7 = 1.75; no distribution; cap = 20 → vorabFull = 1.75/sh × 10 = 17.50.
        var calculator = new GermanTaxCalculator(
            new StubInterestRateProvider((2024, 0.025m)),
            new StubYearStartAndEndPriceProvider(("IE00B6R52259", 2024, 100m, 120m)),
            new StubFxRateLookup());

        var result = calculator.Calculate(new PortfolioLedger([
            new TradeEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero),
                new DateOnly(2024, 1, 15),
                TaxCalculatorTestDoubles.SourceProvenance("BUY-1"),
                instrumentId,
                TradeSide.Buy,
                new Quantity(10m),
                new Money(100m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR)),
            // Sold in 2025 at 80 → already a loss before the Vorabpauschale deduction.
            new TradeEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2025, 2, 1, 9, 0, 0, TimeSpan.Zero),
                new DateOnly(2025, 2, 1),
                TaxCalculatorTestDoubles.SourceProvenance("SELL-1"),
                instrumentId,
                TradeSide.Sell,
                new Quantity(10m),
                new Money(80m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR))
        ]), instruments);

        var vorab = result.Entries.Single(x => x.Type == GermanTaxEntryType.Vorabpauschale);
        Assert.Equal(new DateOnly(2025, 1, 1), vorab.Date);
        Assert.Equal(17.5m, decimal.Round(vorab.RawAmount, 2));

        var sell = result.Entries.Single(x => x.Type == GermanTaxEntryType.Sell);
        Assert.Equal(17.5m, decimal.Round(sell.UsedVorabpauschale, 2));
        // Raw result = proceeds − cost − usedVorab = 800 − 1000 − 17.50 = −217.50 (the prepaid Vorab enlarges the loss).
        Assert.Equal(-217.5m, decimal.Round(sell.RawAmount, 2));
        // Teilfreistellung (30 %) applies to the loss too: −217.50 × 0.70 = −152.25.
        Assert.Equal(-152.25m, decimal.Round(sell.TaxableAmount, 2));
        Assert.True(sell.TaxableAmount < 0m, "a loss that consumed Vorabpauschale must remain a loss");
    }

    [Fact]
    public void Calculate_InterestAndWithholdingTax_ProducesSeparateEntries()
    {
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instruments = new[]
        {
            new Instrument(instrumentId, "", "EUR", "Euro cash", 0m)
        };

        var calculator = new GermanTaxCalculator(
            new StubInterestRateProvider(),
            new StubYearEndPriceProvider(),
            new StubFxRateLookup());

        var result = calculator.Calculate(new PortfolioLedger([
            new CashEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2025, 3, 10, 12, 0, 0, TimeSpan.Zero),
                new DateOnly(2025, 3, 10),
                TaxCalculatorTestDoubles.SourceProvenance("INT-1"),
                instrumentId,
                CashFlowType.Interest,
                new Money(17.42m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR)),
            new CashEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2025, 3, 11, 12, 0, 0, TimeSpan.Zero),
                new DateOnly(2025, 3, 11),
                TaxCalculatorTestDoubles.SourceProvenance("WHT-1"),
                instrumentId,
                CashFlowType.WithholdingTax,
                new Money(-3.11m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                instrumentId)
        ]), instruments);

        var interestEntry = result.Entries.Single(x => x.Type == GermanTaxEntryType.Interest);
        Assert.Equal(17.42m, interestEntry.RawAmount);
        Assert.Equal(17.42m, interestEntry.TaxableAmount);

        var withholdingTaxEntry = result.Entries.Single(x => x.Type == GermanTaxEntryType.WithholdingTax);
        Assert.Equal(-3.11m, withholdingTaxEntry.RawAmount);
        Assert.Equal(3.11m, withholdingTaxEntry.ForeignWithholdingTax);
    }

}
