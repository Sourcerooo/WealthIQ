using WealthIQ.Application.Tax;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Tests.Application.Tax;

/// <summary>
/// Edge-case coverage for the Vorabpauschale calculation (§ 18 InvStG). Every expected value is
/// computed from the statute: Basisertrag = acquisition cost × Basiszins × 0.7 × (months/12),
/// capped at the year's appreciation, reduced by distributions, then the equity Teilfreistellung
/// (30 %) is applied to reach the taxable amount. The Vorabpauschale is deemed received on the
/// first day of the following year.
/// </summary>
public sealed class GermanTaxCalculatorVorabpauschaleTests
{
    private static readonly AccountId Account = AccountId.NewId();
    private static readonly InstrumentId Equity = InstrumentId.NewId();
    private const string Isin = "IE00B3XXRP09";

    private static readonly Instrument[] Catalog =
    [
        new(Equity, Isin, "VUSA", "Vanguard S&P 500", 0.30m)
    ];

    private static GermanTaxCalculator Calculator(decimal basisRate, decimal yearEndPrice) => new(
        new FakeBasisInterestRateProvider((2024, basisRate)),
        new FakeYearEndPriceProvider((Isin, 2024, yearEndPrice)),
        new FakeFxRateLookup());

    private static GermanTaxEntry? SingleVorab(GermanTaxCalculationResult result)
        => result.Entries.Where(x => x.Type == GermanTaxEntryType.Vorabpauschale).Cast<GermanTaxEntry?>().SingleOrDefault();

    [Fact]
    public void Vorabpauschale_CappedByAppreciation_UsesAppreciationNotBasisYield()
    {
        // Basisertrag/share = 100 × 0.05 × 0.7 × 12/12 = 3.50, but appreciation is only 101-100 = 1.00.
        // The Vorabpauschale is capped at the appreciation: 1.00 × 100 = 100.00; taxable = 100 × 0.70 = 70.00.
        var calculator = Calculator(basisRate: 0.05m, yearEndPrice: 101m);
        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, Equity, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "BUY-1")
        ]);

        var vorab = SingleVorab(calculator.Calculate(ledger, Catalog));

        Assert.NotNull(vorab);
        Assert.Equal(new DateOnly(2025, 1, 1), vorab!.Value.Date);
        Assert.Equal(100m, decimal.Round(vorab.Value.RawAmount, 2));
        Assert.Equal(70m, decimal.Round(vorab.Value.TaxableAmount, 2));
    }

    [Fact]
    public void Vorabpauschale_NoAppreciation_ProducesNoEntry()
    {
        // Year-end price below cost → appreciation = 0 → min(basisYield, 0) = 0 → no Vorabpauschale.
        var calculator = Calculator(basisRate: 0.05m, yearEndPrice: 95m);
        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, Equity, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "BUY-1")
        ]);

        var result = calculator.Calculate(ledger, Catalog);

        Assert.Empty(result.Entries.Where(x => x.Type == GermanTaxEntryType.Vorabpauschale));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void Vorabpauschale_ZeroOrNegativeBasisInterestRate_ProducesNoEntry(double basisRate)
    {
        // A negative or zero Basiszins (as Germany had in 2021/2022) yields no Vorabpauschale for the year.
        var calculator = Calculator(basisRate: (decimal)basisRate, yearEndPrice: 200m);
        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, Equity, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "BUY-1")
        ]);

        var result = calculator.Calculate(ledger, Catalog);

        Assert.Empty(result.Entries.Where(x => x.Type == GermanTaxEntryType.Vorabpauschale));
    }

    [Fact]
    public void Vorabpauschale_PartialYearHolding_AppliesZwoelftelung()
    {
        // Acquired in October → only Oct/Nov/Dec count (3/12). Basisertrag/share = 100 × 0.05 × 0.7 × 3/12 = 0.875.
        // Appreciation (200-100) is far larger, so the basis yield wins: 0.875 × 100 = 87.50; taxable = 61.25.
        var calculator = Calculator(basisRate: 0.05m, yearEndPrice: 200m);
        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, Equity, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2024, 10, 15, 10, 0, 0, TimeSpan.Zero), "BUY-1")
        ]);

        var vorab = SingleVorab(calculator.Calculate(ledger, Catalog));

        Assert.NotNull(vorab);
        Assert.Equal(87.5m, decimal.Round(vorab!.Value.RawAmount, 2));
        Assert.Equal(61.25m, decimal.Round(vorab.Value.TaxableAmount, 2));
    }

    [Fact]
    public void Vorabpauschale_DistributionExceedsBasisYield_ProducesNoEntryButKeepsDividend()
    {
        // Basisertrag/share = 3.50, but a 4.00/share distribution during the year fully absorbs it
        // (§ 18 Abs. 1 Satz 2 InvStG), so no Vorabpauschale remains. The dividend itself is still taxed.
        var calculator = Calculator(basisRate: 0.05m, yearEndPrice: 200m);
        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, Equity, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "BUY-1"),
            TaxEntries.Dividend(Account, Equity, Equity, grossAmount: 400m,
                new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero), "DIV-1")
        ]);

        var result = calculator.Calculate(ledger, Catalog);

        Assert.Empty(result.Entries.Where(x => x.Type == GermanTaxEntryType.Vorabpauschale));
        var dividend = Assert.Single(result.Entries.Where(x => x.Type == GermanTaxEntryType.Dividend));
        Assert.Equal(400m, decimal.Round(dividend.RawAmount, 2));
        Assert.Equal(280m, decimal.Round(dividend.TaxableAmount, 2)); // 400 × (1 - 0.30)
    }

    [Fact]
    public void Vorabpauschale_QuietHoldingYearWithNoEntries_StillProducesEntry()
    {
        // Buy 2023, no entries at all in 2024, sale would be later. The 2024 year-end closing must still
        // run, posting a Vorabpauschale deemed received 2025-01-01.
        var calculator = new GermanTaxCalculator(
            new FakeBasisInterestRateProvider((2023, 0.05m), (2024, 0.05m)),
            new FakeYearEndPriceProvider((Isin, 2023, 150m), (Isin, 2024, 200m)),
            new FakeFxRateLookup());

        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, Equity, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2023, 1, 10, 10, 0, 0, TimeSpan.Zero), "BUY-1"),
            // A late 2025 entry establishes the replay range end; 2024 has no entries.
            TaxEntries.Trade(Account, Equity, TradeSide.Buy, 1m, 100m,
                new DateTimeOffset(2025, 6, 10, 10, 0, 0, TimeSpan.Zero), "BUY-2")
        ]);

        var result = calculator.Calculate(ledger, Catalog);

        Assert.Contains(result.Entries,
            e => e.Type == GermanTaxEntryType.Vorabpauschale && e.Date == new DateOnly(2025, 1, 1));
    }
}
