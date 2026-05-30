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
    public void PortfolioLedger_SameTimestamp_OrdersBySourceRecordReference_RegardlessOfInputOrder()
    {
        // Two entries at the SAME instant must be ordered deterministically by their source record
        // reference (the broker transaction id, issued in booking order) — never by the random
        // EntryId GUID. This guards FIFO lot matching against run-to-run non-determinism.
        var sameInstant = new DateTimeOffset(2021, 12, 9, 12, 35, 8, TimeSpan.Zero);

        var entryTx2 = MakeCashEntry(sameInstant, "757965141");
        var entryTx1 = MakeCashEntry(sameInstant, "757965140");

        var ledgerOneOrder = new PortfolioLedger([entryTx2, entryTx1]);
        var ledgerOtherOrder = new PortfolioLedger([entryTx1, entryTx2]);

        Assert.Equal("757965140", ledgerOneOrder.Entries[0].SourceProvenance.SourceRecordReference);
        Assert.Equal("757965141", ledgerOneOrder.Entries[1].SourceProvenance.SourceRecordReference);

        // The order of the input collection must not change the canonical order.
        Assert.Equal(
            ledgerOneOrder.Entries.Select(x => x.SourceProvenance.SourceRecordReference),
            ledgerOtherOrder.Entries.Select(x => x.SourceProvenance.SourceRecordReference));
    }

    [Fact]
    public void PortfolioLedger_OrdersByTimestampFirstThenSourceReference()
    {
        var earlierButHigherRef = MakeCashEntry(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), "ZZZ-9");
        var laterButLowerRef = MakeCashEntry(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero), "AAA-1");

        var ledger = new PortfolioLedger([laterButLowerRef, earlierButHigherRef]);

        // Timestamp dominates the reference tie-break.
        Assert.Equal("ZZZ-9", ledger.Entries[0].SourceProvenance.SourceRecordReference);
        Assert.Equal("AAA-1", ledger.Entries[1].SourceProvenance.SourceRecordReference);
    }

    private static CashEntry MakeCashEntry(DateTimeOffset occurredAt, string sourceReference)
        => new(
            PortfolioEntryId.NewId(),
            AccountId.NewId(),
            occurredAt,
            DateOnly.FromDateTime(occurredAt.UtcDateTime),
            new SourceProvenance
            {
                SourceSystem = "IBKR",
                ImportFormat = "XML",
                SourceLocation = "sample.xml",
                SourceRecordReference = sourceReference
            },
            InstrumentId.NewId(),
            CashFlowType.Interest,
            new Money(1m, Currency.EUR),
            new Money(0m, Currency.EUR),
            new Money(0m, Currency.EUR));

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
