using WealthIQ.Application.Matcher;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Event;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Lot;

namespace WealthIQ.Tests.Application.Matcher;

public class FiFoMatcherTest
{
    [Fact]
    public void Match_FullCloseLong_CreatesOneConsumptionAndNoRemainderLot()
    {

        // Arrange
        var openLots = new List<OpenLot>();
        openLots.Add(
            new OpenLot
            {
                LotId = new LotId(Guid.NewGuid()),
                AccountId = new AccountId("ACC-1"),
                InstrumentId = new InstrumentId("AAPL"),
                OpenEventId = Guid.NewGuid(),
                OpenTradeDate = new DateOnly(2025, 1, 10),
                Direction = PositionDirection.Long,
                OriginalQuantity = new Quantity(100m),
                RemainingQuantity = new Quantity(100m),
                OpenUnitPrice = new Money(100m, Currency.EUR),
                RemainingOpenFees = new Money(10m, Currency.EUR),
                RemainingOpenTaxes = new Money(4m, Currency.EUR)
            }
         );
        var tradeEvent = new ExecutedTradeEvent(
            EventId: Guid.NewGuid(),
            AccountId: new AccountId("ACC-1"),
            OccuredAt: DateTime.UtcNow,
            SourceBroker: "BrokerX",
            SourceReference: "REF-123",
            InstrumentId: new InstrumentId("AAPL"),
            Side: TradeSide.Sell,
            Quantity: new Quantity(100m),
            UnitPrice: new Money(150m, Currency.EUR),
            Fees: new Money(12m, Currency.EUR),
            Taxes: new Money(4.8m, Currency.EUR)
        );

        // Act
        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        // Assert
        Assert.Null(result.NewlyOpenedRemainderLot);
        var openLot = result.UpdatedOpenLots.First();
        Assert.Equal(0m, openLot.RemainingQuantity.Value);
        Assert.Single(result.Consumptions);
        var consumption = result.Consumptions.First();
        Assert.Equal(100m, consumption.MatchedQuantity.Value);
        Assert.Equal(4969.20m, consumption.RealizedPnL.Amount);
    }


    [Fact]
    public void Match_PartialCloseLong_ReducesOpenLotAndCalculatesProRataCosts()
    {
        // Arrange
        // Setup: 1 Open Long Lot (100), Sell Event (40), Fees/Taxes auf Open und Close gesetzt
        var openLots = new List<OpenLot>();
        openLots.Add(
            new OpenLot
            {
                LotId = new LotId(Guid.NewGuid()),
                AccountId = new AccountId("ACC-1"),
                InstrumentId = new InstrumentId("AAPL"),
                OpenEventId = Guid.NewGuid(),
                OpenTradeDate = new DateOnly(2025, 1, 10),
                Direction = PositionDirection.Long,
                OriginalQuantity = new Quantity(100m),
                RemainingQuantity = new Quantity(100m),
                OpenUnitPrice = new Money(100m, Currency.EUR),
                RemainingOpenFees = new Money(10m, Currency.EUR),
                RemainingOpenTaxes = new Money(4m, Currency.EUR)
            }
         );

        var tradeEvent = new ExecutedTradeEvent(
            EventId: Guid.NewGuid(),
            AccountId: new AccountId("ACC-1"),
            OccuredAt: DateTime.UtcNow,
            SourceBroker: "BrokerX",
            SourceReference: "REF-123",
            InstrumentId: new InstrumentId("AAPL"),
            Side: TradeSide.Sell,
            Quantity: new Quantity(40m),
            UnitPrice: new Money(200m, Currency.EUR),
            Fees: new Money(20m, Currency.EUR),
            Taxes: new Money(8m, Currency.EUR)
        );

        // Act
        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        // Assert
        // Erwartung: RemainingQuantity = 60, Open/Close Fees/Taxes korrekt pro-rata allokiert.
        Assert.Null(result.NewlyOpenedRemainderLot);
        var openLot = result.UpdatedOpenLots.First();
        Assert.Equal(60m, openLot.RemainingQuantity.Value);
        Assert.Single(result.Consumptions);
        var consumption = result.Consumptions.First();
        Assert.Equal(40m, consumption.MatchedQuantity.Value);
        Assert.Equal(4m, consumption.AllocatedOpenFees.Amount);
        Assert.Equal(1.6m, consumption.AllocatedOpenTaxes.Amount);
        Assert.Equal(20m, consumption.AllocatedCloseFees.Amount);
        Assert.Equal(8m, consumption.AllocatedCloseTaxes.Amount);
    }

    [Fact]
    public void Match_CloseAcrossTwoLots_UsesFifoOrder()
    {

        // Arrange
        // Setup: zwei Open Long Lots gleicher Instrument/Account, älteres Lot zuerst,
        // Sell Event über Gesamtmenge > Lot1 und < Lot1+Lot2
        var openLots = new List<OpenLot>();
        openLots.Add(
            new OpenLot
            {
                LotId = new LotId(Guid.NewGuid()),
                AccountId = new AccountId("ACC-1"),
                InstrumentId = new InstrumentId("AAPL"),
                OpenEventId = Guid.NewGuid(),
                OpenTradeDate = new DateOnly(2025, 1, 10),
                Direction = PositionDirection.Long,
                OriginalQuantity = new Quantity(200m),
                RemainingQuantity = new Quantity(200m),
                OpenUnitPrice = new Money(100m, Currency.EUR),
                RemainingOpenFees = new Money(10m, Currency.EUR),
                RemainingOpenTaxes = new Money(4m, Currency.EUR)
            }
         );
        openLots.Add(
            new OpenLot
            {
                LotId = new LotId(Guid.NewGuid()),
                AccountId = new AccountId("ACC-1"),
                InstrumentId = new InstrumentId("AAPL"),
                OpenEventId = Guid.NewGuid(),
                OpenTradeDate = new DateOnly(2025, 1, 9),
                Direction = PositionDirection.Long,
                OriginalQuantity = new Quantity(100m),
                RemainingQuantity = new Quantity(100m),
                OpenUnitPrice = new Money(50m, Currency.EUR),
                RemainingOpenFees = new Money(8m, Currency.EUR),
                RemainingOpenTaxes = new Money(3m, Currency.EUR)
            }
         );

        var tradeEvent = new ExecutedTradeEvent(
            EventId: Guid.NewGuid(),
            AccountId: new AccountId("ACC-1"),
            OccuredAt: DateTime.UtcNow,
            SourceBroker: "BrokerX",
            SourceReference: "REF-123",
            InstrumentId: new InstrumentId("AAPL"),
            Side: TradeSide.Sell,
            Quantity: new Quantity(150m),
            UnitPrice: new Money(200m, Currency.EUR),
            Fees: new Money(21m, Currency.EUR),
            Taxes: new Money(9m, Currency.EUR)
        );

        // Act
        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        // Assert
        // Erwartung: erstes Lot wird zuerst konsumiert, dann zweites (FIFO-Reihenfolge explizit prüfen).
        Assert.Null(result.NewlyOpenedRemainderLot);
        var openLot = result.UpdatedOpenLots[0];
        Assert.Equal(0m, openLot.RemainingQuantity.Value);

        openLot = result.UpdatedOpenLots[1];
        Assert.Equal(150m, openLot.RemainingQuantity.Value);
        Assert.Equal(2m, result.Consumptions.Count);

        var consumption = result.Consumptions[0];
        Assert.Equal(100m, consumption.MatchedQuantity.Value);
        Assert.Equal(8m, consumption.AllocatedOpenFees.Amount);
        Assert.Equal(3m, consumption.AllocatedOpenTaxes.Amount);
        Assert.Equal(14m, decimal.Round(consumption.AllocatedCloseFees.Amount, 2));
        Assert.Equal(6m, decimal.Round(consumption.AllocatedCloseTaxes.Amount, 2));

        consumption = result.Consumptions[1];
        Assert.Equal(50m, consumption.MatchedQuantity.Value);
        Assert.Equal(2.5m, consumption.AllocatedOpenFees.Amount);
        Assert.Equal(1m, consumption.AllocatedOpenTaxes.Amount);
        Assert.Equal(7m, decimal.Round(consumption.AllocatedCloseFees.Amount, 2));
        Assert.Equal(3m, decimal.Round(consumption.AllocatedCloseTaxes.Amount, 2));
    }

    [Fact]
    public void Match_SellWithoutOpenLong_CreatesNewShortOpenLot()
    {

        // Arrange
        // Setup: keine passenden Open Long Lots, Sell Event
        var openLots = new List<OpenLot>();

        var tradeEvent = new ExecutedTradeEvent(
            EventId: Guid.NewGuid(),
            AccountId: new AccountId("ACC-1"),
            OccuredAt: DateTime.UtcNow,
            SourceBroker: "BrokerX",
            SourceReference: "REF-123",
            InstrumentId: new InstrumentId("AAPL"),
            Side: TradeSide.Sell,
            Quantity: new Quantity(100m),
            UnitPrice: new Money(200m, Currency.EUR),
            Fees: new Money(10m, Currency.EUR),
            Taxes: new Money(4m, Currency.EUR)
        );

        // Act
        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        // Assert
        // Erwartung: keine Consumptions, NewlyOpenedRemainderLot vorhanden mit Direction = Short.
        Assert.Equal(0m, result.Consumptions.Count);
        Assert.NotNull(result.NewlyOpenedRemainderLot);
        var openLot = result.NewlyOpenedRemainderLot;
        Assert.Equal(PositionDirection.Short, openLot.Direction);
        Assert.Equal(100m, openLot.RemainingQuantity.Value);
    }

    [Fact]
    public void Match_BuyWithoutOpenShort_CreatesNewLongOpenLot()
    {
        // Arrange
        // Setup: keine passenden Open Short Lots, Buy Event
        var openLots = new List<OpenLot>();

        var tradeEvent = new ExecutedTradeEvent(
            EventId: Guid.NewGuid(),
            AccountId: new AccountId("ACC-1"),
            OccuredAt: DateTime.UtcNow,
            SourceBroker: "BrokerX",
            SourceReference: "REF-123",
            InstrumentId: new InstrumentId("AAPL"),
            Side: TradeSide.Buy,
            Quantity: new Quantity(100m),
            UnitPrice: new Money(200m, Currency.EUR),
            Fees: new Money(10m, Currency.EUR),
            Taxes: new Money(4m, Currency.EUR)
        );

        // Act
        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        // Assert
        //  Erwartung: keine Consumptions, NewlyOpenedRemainderLot vorhanden mit Direction = Long.
        Assert.Equal(0m, result.Consumptions.Count);
        Assert.NotNull(result.NewlyOpenedRemainderLot);
        var openLot = result.NewlyOpenedRemainderLot;
        Assert.Equal(PositionDirection.Long, openLot.Direction);
        Assert.Equal(100m, openLot.RemainingQuantity.Value);
    }

    [Fact]
    public void Match_OverCloseLong_ClosesExistingAndOpensShortRemainder()
    {

        // Arrange
        // Setup: Open Long 50, Sell 80

        // Act

        // Assert
        // Erwartung: 50 werden geschlossen, Rest 30 als neues Short-Lot
    }

    [Fact]
    public void Match_IgnoresLotsFromDifferentAccountOrInstrument()
    {

        // Arrange
        // Setup: Lots mit anderem AccountId oder InstrumentId

        // Act

        // Assert
        // Erwartung: diese Lots werden nicht gematcht.
    }

    [Fact]
    public void Consumption_LongPosition_PnLIsCalculatedCorrectly()
    {
        // Arrange
        // Setup: Long Open 100 @ 100, Close @ 150, definierte Fees/ Taxes

        // Act

        // Assert
        // Erwartung: Gross, Net, CostBasis, Proceeds gemäß Formel korrekt.
    }

    [Fact]
    public void Consumption_ShortPosition_PnLIsCalculatedCorrectly()
    {
        // Arrange
        //  Setup: Short Open (durch vorherigen Sell), später Buy-to-cover

        // Act

        // Assert
        //Erwartung: Short-Formel korrekt (Gewinn bei niedrigerem Rückkaufpreis).

    }
}
