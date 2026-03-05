using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Event;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Lot;

namespace WealthIQ.Tests.Domain.Lot;

public class OpenLotTests
{
    [Fact]
    public void Consume_PartialClose_UpdatesRemainingQuantityAndAllocatedCosts()
    {
        var lot = new OpenLot
        {
            LotId = LotId.NewId(),
            AccountId = AccountId.NewId(),
            InstrumentId = InstrumentId.NewId(),
            OpenEventId = AccountEventId.NewId(),
            OpenTradeDate = new DateOnly(2025, 1, 10),
            Direction = PositionDirection.Long,
            OriginalQuantity = new Quantity(100m),
            RemainingQuantity = new Quantity(100m),
            OpenUnitPrice = new Money(100m, Currency.EUR),
            RemainingOpenFees = new Money(10m, Currency.EUR),
            RemainingOpenTaxes = new Money(4m, Currency.EUR)
        };

        var updatedLot = lot.Consume(new Quantity(40m));

        Assert.Equal(60m, updatedLot.RemainingQuantity.Value);
        Assert.Equal(6m, updatedLot.RemainingOpenFees.Amount);
        Assert.Equal(2.4m, updatedLot.RemainingOpenTaxes.Amount);
        Assert.False(updatedLot.IsClosed);
    }

    [Fact]
    public void Consume_QuantityGreaterThanRemaining_ThrowsInvalidOperationException()
    {
        var lot = new OpenLot
        {
            LotId = LotId.NewId(),
            AccountId = AccountId.NewId(),
            InstrumentId = InstrumentId.NewId(),
            OpenEventId = AccountEventId.NewId(),
            OpenTradeDate = new DateOnly(2025, 1, 10),
            Direction = PositionDirection.Long,
            OriginalQuantity = new Quantity(100m),
            RemainingQuantity = new Quantity(50m),
            OpenUnitPrice = new Money(100m, Currency.EUR),
            RemainingOpenFees = new Money(5m, Currency.EUR),
            RemainingOpenTaxes = new Money(2m, Currency.EUR)
        };

        Assert.Throws<InvalidOperationException>(() => lot.Consume(new Quantity(60m)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Consume_ZeroOrNegativeQuantity_ThrowsInvalidOperationException(decimal quantity)
    {
        var lot = new OpenLot
        {
            LotId = LotId.NewId(),
            AccountId = AccountId.NewId(),
            InstrumentId = InstrumentId.NewId(),
            OpenEventId = AccountEventId.NewId(),
            OpenTradeDate = new DateOnly(2025, 1, 10),
            Direction = PositionDirection.Long,
            OriginalQuantity = new Quantity(100m),
            RemainingQuantity = new Quantity(50m),
            OpenUnitPrice = new Money(100m, Currency.EUR),
            RemainingOpenFees = new Money(5m, Currency.EUR),
            RemainingOpenTaxes = new Money(2m, Currency.EUR)
        };

        Assert.Throws<InvalidOperationException>(() => lot.Consume(new Quantity(quantity)));
    }
}
