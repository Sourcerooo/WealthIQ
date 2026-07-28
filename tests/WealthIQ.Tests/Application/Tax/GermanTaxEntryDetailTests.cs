using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Tax;

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
        var (_, result) = await TaxFixture.CalculateAsync();
        return result.Entries;
    }
}
