using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Tests.Domain.Ledger;

public sealed class CanonicalPortfolioLedgerTests
{
    [Fact]
    public void TradeEntry_ZeroQuantity_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => new TradeEntry(
            PortfolioEntryId.NewId(),
            AccountId.NewId(),
            DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow),
            CreateSourceProvenance(),
            InstrumentId.NewId(),
            TradeSide.Buy,
            new Quantity(0m),
            new Money(100m, Currency.USD),
            new Money(1m, Currency.USD),
            new Money(0m, Currency.USD)));
    }

    [Fact]
    public void AssetTransferEntry_WithoutQuantityOrAmount_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => new AssetTransferEntry(
            PortfolioEntryId.NewId(),
            AccountId.NewId(),
            DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow),
            CreateSourceProvenance(),
            AssetTransferType.Internal));
    }

    [Fact]
    public void PortfolioLedger_SortsEntriesByOccurredAt()
    {
        var accountId = AccountId.NewId();
        var sourceProvenance = CreateSourceProvenance();

        var laterEntry = new CashEntry(
            PortfolioEntryId.NewId(),
            accountId,
            new DateTimeOffset(2025, 2, 2, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2025, 2, 2),
            sourceProvenance,
            InstrumentId.NewId(),
            CashFlowType.Dividend,
            new Money(10m, Currency.EUR),
            new Money(0m, Currency.EUR),
            new Money(0m, Currency.EUR));

        var earlierEntry = new CashEntry(
            PortfolioEntryId.NewId(),
            accountId,
            new DateTimeOffset(2025, 1, 2, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2025, 1, 2),
            sourceProvenance,
            InstrumentId.NewId(),
            CashFlowType.Interest,
            new Money(5m, Currency.EUR),
            new Money(0m, Currency.EUR),
            new Money(0m, Currency.EUR));

        var ledger = new PortfolioLedger([laterEntry, earlierEntry]);

        Assert.Equal(earlierEntry.EntryId, ledger.Entries[0].EntryId);
        Assert.Equal(laterEntry.EntryId, ledger.Entries[1].EntryId);
    }

    [Fact]
    public void TradeEntry_KeepsSourceCurrencyFactsWithoutConversionMetadata()
    {
        var entry = new TradeEntry(
            PortfolioEntryId.NewId(),
            AccountId.NewId(),
            DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow),
            CreateSourceProvenance(),
            InstrumentId.NewId(),
            TradeSide.Buy,
            new Quantity(3m),
            new Money(100m, Currency.USD),
            new Money(1m, Currency.USD),
            new Money(0m, Currency.USD));

        Assert.Equal(Currency.USD, entry.UnitPrice.Currency);
        Assert.Equal(Currency.USD, entry.Fees.Currency);
        Assert.Equal(Currency.USD, entry.Taxes.Currency);
    }

    private static SourceProvenance CreateSourceProvenance()
        => new()
        {
            SourceSystem = "IBKR",
            ImportFormat = "XML",
            SourceLocation = "sample.xml",
            SourceRecordReference = "TX-1"
        };
}
