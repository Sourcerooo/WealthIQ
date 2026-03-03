using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Event;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Lot;

namespace WealthIQ.Tests.Domain.Lot;

public class OpenLotTests
{
    private Account GetAccount() => new Account(AccountId: AccountId.NewId(), AccountNumber: "12345");
    private Instrument GetInstrument() => new Instrument(
        InstrumentId: InstrumentId.NewId(),
        ISIN: "US0378331005",
        Symbol: "AAPL",
        Name: "Apple Inc.",
        Teilfreistellungsquote: 0m);

    private ExecutedTradeEvent GetTradeEvent() => new ExecutedTradeEvent(
            EventId: AccountEventId.NewId(),
            Account: GetAccount(),
            OccuredAt: DateTime.UtcNow,
            SourceBroker: "Source Broker",
            SourceReference: "Source Broker Reference",
            Instrument: GetInstrument(),
            Side: TradeSide.Buy,
            Quantity: new Quantity(100m),
            UnitPrice: new Money(100m, Currency.EUR),
            Fees: new Money(10m, Currency.EUR),
            Taxes: new Money(4m, Currency.EUR)
        );

    [Fact]
    public void Consume_PartialClose_UpdatesRemainingQuantityAndAllocatedCosts()
    {
        // Arrange
        var lot = new OpenLot
        {
            LotId = LotId.NewId(),
            Account = GetAccount(),
            Instrument = GetInstrument(),
            OpenEvent = GetTradeEvent(),
            OpenTradeDate = new DateOnly(2025, 1, 10),
            Direction = PositionDirection.Long,
            OriginalQuantity = new Quantity(100m),
            RemainingQuantity = new Quantity(100m),
            OpenUnitPrice = new Money(100m, Currency.EUR),
            RemainingOpenFees = new Money(10m, Currency.EUR),
            RemainingOpenTaxes = new Money(4m, Currency.EUR)
        };

        // Act
        var updatedLot = lot.Consume(new Quantity(40m));

        // Assert
        Assert.Equal(60m, updatedLot.RemainingQuantity.Value);
        Assert.Equal(6m, updatedLot.RemainingOpenFees.Amount);
        Assert.Equal(2.4m, updatedLot.RemainingOpenTaxes.Amount);
        Assert.False(updatedLot.IsClosed);
    }

    [Fact]
    public void Consume_QuantityGreaterThanRemaining_ThrowsInvalidOperationException()
    {
        // Arrange
        var lot = new OpenLot
        {
            LotId = new LotId(Guid.NewGuid()),
            Account = GetAccount(),
            Instrument = GetInstrument(),
            OpenEvent = GetTradeEvent(),
            OpenTradeDate = new DateOnly(2025, 1, 10),
            Direction = PositionDirection.Long,
            OriginalQuantity = new Quantity(100m),
            RemainingQuantity = new Quantity(50m),
            OpenUnitPrice = new Money(100m, Currency.EUR),
            RemainingOpenFees = new Money(5m, Currency.EUR),
            RemainingOpenTaxes = new Money(2m, Currency.EUR)
        };

        // Act + Assert
        Assert.Throws<InvalidOperationException>(() => lot.Consume(new Quantity(60m)));
    }

    [Fact]
    public void Consume_ZeroOrNegativeQuantity_ThrowsInvalidOperationException()
    {
        // Arrange
        // Setup: OpenLot.Consume(0) und < 0

        // Act

        // Assert
        // Erwartung: jeweils Exception.
    }
}
