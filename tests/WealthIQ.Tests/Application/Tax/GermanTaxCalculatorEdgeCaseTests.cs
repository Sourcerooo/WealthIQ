using WealthIQ.Application.Tax;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Domain.Model.Tax;
using Xunit;

namespace WealthIQ.Tests.Application.Tax;

public sealed class GermanTaxCalculatorEdgeCaseTests
{
    private static readonly AccountId Account = AccountId.NewId();
    private const string Isin = "IE00B3XXRP09";

    private static GermanTaxCalculator Calculator(
        FakeBasisInterestRateProvider? rates = null,
        FakeYearEndPriceProvider? prices = null)
        => new(rates ?? new FakeBasisInterestRateProvider(), prices ?? new FakeYearEndPriceProvider(), new FakeFxRateLookup());

    [Fact]
    public void Calculate_EmptyLedger_ProducesNoEntriesOrLots()
    {
        var result = Calculator().Calculate(new PortfolioLedger([]), []);

        Assert.Empty(result.Entries);
        Assert.Empty(result.OpenLots);
    }

    [Fact]
    public void Calculate_SellConsumesInstrumentMissingFromCatalog_Throws()
    {
        var unknown = InstrumentId.NewId();
        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, unknown, TradeSide.Buy, 10m, 100m,
                new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "BUY-1"),
            TaxEntries.Trade(Account, unknown, TradeSide.Sell, 10m, 120m,
                new DateTimeOffset(2024, 2, 10, 10, 0, 0, TimeSpan.Zero), "SELL-1")
        ]);

        Assert.Throws<InvalidOperationException>(() => Calculator().Calculate(ledger, []));
    }

    [Fact]
    public void Calculate_DividendWithoutRelatedInstrument_Throws()
    {
        var cashInstrument = InstrumentId.NewId();
        var ledger = new PortfolioLedger([
            new CashEntry(
                PortfolioEntryId.NewId(),
                Account,
                new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero),
                new DateOnly(2024, 6, 10),
                TaxEntries.Provenance("DIV-1"),
                cashInstrument,
                CashFlowType.Dividend,
                new Money(50m, Currency.EUR),
                new Money(0m, Currency.EUR),
                new Money(0m, Currency.EUR),
                relatedInstrumentId: null)
        ]);

        var instruments = new[] { new Instrument(cashInstrument, "", "EUR", "Euro cash", 0m) };

        Assert.Throws<InvalidOperationException>(() => Calculator().Calculate(ledger, instruments));
    }

    [Fact]
    public void Calculate_MissingYearEndPrice_WhenVorabRequired_Throws()
    {
        var instrumentId = InstrumentId.NewId();
        var instruments = new[] { new Instrument(instrumentId, Isin, "VUSA", "Vanguard", 0.30m) };
        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, instrumentId, TradeSide.Buy, 100m, 100m,
                new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero), "BUY-1")
        ]);

        // Basiszins is positive and a long fund lot is held over year-end, so the year-end price is
        // required. It is not configured → fail-fast (CLAUDE.md: missing required price data is blocking).
        var calculator = Calculator(new FakeBasisInterestRateProvider((2024, 0.05m)), new FakeYearEndPriceProvider());

        Assert.Throws<InvalidOperationException>(() => calculator.Calculate(ledger, instruments));
    }

    [Fact]
    public void Calculate_UnsupportedEntryType_Throws()
    {
        var instrumentId = InstrumentId.NewId();
        var instruments = new[] { new Instrument(instrumentId, Isin, "VUSA", "Vanguard", 0.30m) };

        // AssetTransferEntry is a valid canonical entry no importer currently produces. Tax replay
        // must not silently ignore it — it must fail fast.
        var transfer = new AssetTransferEntry(
            PortfolioEntryId.NewId(),
            Account,
            new DateTimeOffset(2024, 4, 1, 10, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 4, 1),
            TaxEntries.Provenance("XFER-1"),
            AssetTransferType.Incoming,
            instrumentId,
            new Quantity(10m));

        var ledger = new PortfolioLedger([transfer]);

        Assert.Throws<NotSupportedException>(() => Calculator().Calculate(ledger, instruments));
    }

    [Fact]
    public void Calculate_SellWithoutOpenLong_OpensShortAndProducesNoDisposal()
    {
        var instrumentId = InstrumentId.NewId();
        var instruments = new[] { new Instrument(instrumentId, Isin, "VUSA", "Vanguard", 0.30m) };
        var ledger = new PortfolioLedger([
            TaxEntries.Trade(Account, instrumentId, TradeSide.Sell, 10m, 100m,
                new DateTimeOffset(2024, 2, 10, 10, 0, 0, TimeSpan.Zero), "SELL-1")
        ]);

        var result = Calculator().Calculate(ledger, instruments);

        Assert.Empty(result.Entries.Where(x => x.Type == GermanTaxEntryType.Sell));
        var openLot = Assert.Single(result.OpenLots);
        Assert.Equal(PositionDirection.Short, openLot.Direction);
        Assert.Equal(10m, openLot.RemainingQuantity.Value);
    }
}
