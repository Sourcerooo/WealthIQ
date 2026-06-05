using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Persistence.Mapping;
using Xunit;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Infrastructure.Persistence;

public sealed class PortfolioEntryMapperTests
{
    private static SourceProvenance Provenance(string reference) => new()
    {
        SourceSystem = "IBKR",
        ImportFormat = "XML",
        SourceLocation = "file.xml",
        SourceRecordReference = reference
    };

    [Fact]
    public void ToRow_ToDomain_TradeEntry_RoundTrips()
    {
        var original = new TradeEntry(
            PortfolioEntryId.NewId(),
            AccountId.NewId(),
            new DateTimeOffset(2024, 3, 1, 14, 30, 0, TimeSpan.Zero),
            new DateOnly(2024, 3, 1),
            Provenance("TR-1"),
            InstrumentId.NewId(),
            TradeSide.Buy,
            new Quantity(10m),
            new Money(100.50m, CurrencyCode.USD),
            new Money(1.25m, CurrencyCode.USD),
            new Money(0m, CurrencyCode.USD));

        var row = PortfolioEntryMapper.ToRow(original);
        var restored = PortfolioEntryMapper.ToDomain(row);

        Assert.Equal(original, restored);
        Assert.Equal("Trade", row.Category);
        Assert.Equal("IBKR", row.SourceSystem);
        Assert.Equal("TR-1", row.SourceRecordReference);
    }

    [Fact]
    public void ToRow_ToDomain_CashEntry_RoundTrips()
    {
        var original = new CashEntry(
            PortfolioEntryId.NewId(),
            AccountId.NewId(),
            new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 6, 15),
            Provenance("CT-9"),
            InstrumentId.NewId(),
            CashFlowType.Dividend,
            new Money(42m, CurrencyCode.USD),
            new Money(0m, CurrencyCode.USD),
            new Money(6.30m, CurrencyCode.USD));

        var row = PortfolioEntryMapper.ToRow(original);
        var restored = PortfolioEntryMapper.ToDomain(row);

        Assert.Equal(original, restored);
        Assert.Equal("Cash", row.Category);
    }
}
