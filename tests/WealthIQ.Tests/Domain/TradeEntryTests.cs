using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using Xunit;

namespace WealthIQ.Tests.Domain;

public sealed class TradeEntryTests
{
    private static SourceProvenance Prov() => new()
    {
        SourceSystem = "TEST",
        ImportFormat = "TEST",
        SourceLocation = "test",
        SourceRecordReference = "ref-1"
    };

    [Fact]
    public void Constructor_WithoutWithheldTax_DefaultsToZeroEur()
    {
        var entry = new TradeEntry(
            PortfolioEntryId.NewId(), AccountId.NewId(),
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateOnly(2025, 1, 1), Prov(), InstrumentId.NewId(),
            TradeSide.Sell, new Quantity(10m), new Money(100m, Currency.EUR),
            new Money(0m, Currency.EUR), new Money(0m, Currency.EUR));

        Assert.Equal(0m, entry.WithheldTax.Amount);
        Assert.Equal(Currency.EUR, entry.WithheldTax.Currency);
    }

    [Fact]
    public void Constructor_NegativeWithheldTax_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new TradeEntry(
            PortfolioEntryId.NewId(), AccountId.NewId(),
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateOnly(2025, 1, 1), Prov(), InstrumentId.NewId(),
            TradeSide.Sell, new Quantity(10m), new Money(100m, Currency.EUR),
            new Money(0m, Currency.EUR), new Money(0m, Currency.EUR),
            new Money(-1m, Currency.EUR)));
    }

    [Fact]
    public void Constructor_WithWithheldTax_StoresIt()
    {
        var entry = new TradeEntry(
            PortfolioEntryId.NewId(), AccountId.NewId(),
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateOnly(2025, 1, 1), Prov(), InstrumentId.NewId(),
            TradeSide.Sell, new Quantity(10m), new Money(100m, Currency.EUR),
            new Money(0m, Currency.EUR), new Money(0m, Currency.EUR),
            new Money(340.29m, Currency.EUR));

        Assert.Equal(340.29m, entry.WithheldTax.Amount);
    }
}
