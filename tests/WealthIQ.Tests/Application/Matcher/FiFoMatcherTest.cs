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
        var account = GetAccount();
        var instrument = GetInstrument();
        // Arrange
        var openLots = new List<OpenLot> { CreateOpenLot(account: account,
                instrument: instrument) };
        var tradeEvent = CreateTradeEvent(
            account: account,
            instrument: instrument);

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
        var account = GetAccount();
        var instrument = GetInstrument();
        var openLots = new List<OpenLot> { CreateOpenLot(account: account,
                instrument: instrument) };

        var tradeEvent = CreateTradeEvent(
            account: account,
            instrument: instrument,
            quantity: 40m,
            unitPrice: 200m,
            fees: 20m,
            taxes: 8m
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
        var account = GetAccount();
        var instrument = GetInstrument();
        var openLots = new List<OpenLot>
        {
            CreateOpenLot(originalQuantity: 200m,
            account: account,
            instrument: instrument,
            openTradeDate: new DateOnly(2025, 1, 10)),
            CreateOpenLot(
                openTradeDate: new DateOnly(2025, 1, 9),
                account: account,
                instrument: instrument,
                openUnitPrice: 50m,
                remainingOpenFees: 8m,
                remainingOpenTaxes: 3m
            )
        };

        var tradeEvent = CreateTradeEvent(
            account: account,
            instrument: instrument,
            quantity: 150m,
            unitPrice: 200m,
            fees: 21m,
            taxes: 9m);

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

        var tradeEvent = CreateTradeEvent(unitPrice: 200m, fees: 10m, taxes: 4m);

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

        var tradeEvent = CreateTradeEvent(side: TradeSide.Buy, unitPrice: 200m, fees: 10m, taxes: 4m);

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
        var account = GetAccount();
        var instrument = GetInstrument();
        var openLots = new List<OpenLot>
        {
            CreateOpenLot(originalQuantity: 50m,
            account: account,
            instrument: instrument)
        };

        var tradeEvent = CreateTradeEvent(
            side: TradeSide.Sell,
            account: account,
            instrument: instrument,
            quantity: 80m,
            unitPrice: 200m,
            fees: 8m,
            taxes: 4m
        );

        // Act
        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        // Assert
        // Erwartung: 50 werden geschlossen, Rest 30 als neues Short-Lot
        Assert.Single(result.Consumptions);
        var consumption = result.Consumptions[0];
        Assert.Equal(50m, consumption.MatchedQuantity.Value);

        var updatedOpenLot = result.UpdatedOpenLots.Single();
        Assert.Equal(0m, updatedOpenLot.RemainingQuantity.Value);

        Assert.NotNull(result.NewlyOpenedRemainderLot);
        var remainderLot = result.NewlyOpenedRemainderLot;
        Assert.Equal(PositionDirection.Short, remainderLot.Direction);
        Assert.Equal(30m, remainderLot.OriginalQuantity.Value);
        Assert.Equal(30m, remainderLot.RemainingQuantity.Value);
        Assert.Equal(3m, remainderLot.RemainingOpenFees.Amount);
        Assert.Equal(1.5m, remainderLot.RemainingOpenTaxes.Amount);
    }

    [Fact]
    public void Match_IgnoresLotsFromDifferentAccountOrInstrument()
    {

        // Arrange
        // Setup: Lots mit anderem AccountId oder InstrumentId
        var account = GetAccount();
        var instrument = GetInstrument();
        var account2 = GetAccount();
        var instrument2 = GetInstrument();
        var differentAccountLot = CreateOpenLot(account: account, originalQuantity: 70m);
        var differentInstrumentLot = CreateOpenLot(instrument: instrument, originalQuantity: 80m);
        var matchingLot = CreateOpenLot(account: account2,
                instrument: instrument2,
                originalQuantity: 60m);
        var openLots = new List<OpenLot> { differentAccountLot, differentInstrumentLot, matchingLot };

        var tradeEvent = CreateTradeEvent(account: account2,
                instrument: instrument2,
                quantity: 50m);

        // Act
        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        // Assert
        // Erwartung: diese Lots werden nicht gematcht.
        Assert.Single(result.Consumptions);
        var consumption = result.Consumptions[0];
        Assert.Equal(matchingLot.LotId, consumption.OpenLot.LotId);
        Assert.Equal(50m, consumption.MatchedQuantity.Value);

        var updatedDifferentAccountLot = result.UpdatedOpenLots.Single(x => x.LotId == differentAccountLot.LotId);
        Assert.Equal(70m, updatedDifferentAccountLot.RemainingQuantity.Value);

        var updatedDifferentInstrumentLot = result.UpdatedOpenLots.Single(x => x.LotId == differentInstrumentLot.LotId);
        Assert.Equal(80m, updatedDifferentInstrumentLot.RemainingQuantity.Value);

        var updatedMatchingLot = result.UpdatedOpenLots.Single(x => x.LotId == matchingLot.LotId);
        Assert.Equal(10m, updatedMatchingLot.RemainingQuantity.Value);

        Assert.Null(result.NewlyOpenedRemainderLot);
    }

    [Fact]
    public void Consumption_LongPosition_PnLIsCalculatedCorrectly()
    {
        // Arrange
        // Setup: Long Open 100 @ 100, Close @ 150, definierte Fees/ Taxes
        var instrument = GetInstrument();
        var account = GetAccount();
        var openLots = new List<OpenLot>
        {
            CreateOpenLot(
                direction: PositionDirection.Long,
                account: account,
                instrument: instrument,
                originalQuantity: 100m,
                openUnitPrice: 100m,
                remainingOpenFees: 10m,
                remainingOpenTaxes: 4m)
        };

        var tradeEvent = CreateTradeEvent(
            side: TradeSide.Sell,
            account: account,
            instrument: instrument,
            quantity: 100m,
            unitPrice: 150m,
            fees: 12m,
            taxes: 4.8m
        );

        // Act
        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        // Assert
        // Erwartung: Gross, Net, CostBasis, Proceeds gemäß Formel korrekt.
        Assert.Single(result.Consumptions);
        var consumption = result.Consumptions[0];

        var grossPnL = (consumption.CloseUnitPrice.Amount
            - consumption.OpenUnitPrice.Amount) * consumption.MatchedQuantity.Value;
        var netPnL = grossPnL
            - consumption.AllocatedOpenFees.Amount
            - consumption.AllocatedOpenTaxes.Amount
            - consumption.AllocatedCloseFees.Amount
            - consumption.AllocatedCloseTaxes.Amount;

        Assert.Equal(5000m, grossPnL);
        Assert.Equal(4969.2m, netPnL);
        Assert.Equal(10014m, consumption.CostBasis.Amount);
        Assert.Equal(14983.2m, consumption.Proceeds.Amount);
        Assert.Equal(4969.2m, consumption.RealizedPnL.Amount);
    }

    [Fact]
    public void Consumption_ShortPosition_PnLIsCalculatedCorrectly()
    {
        // Arrange
        //  Setup: Short Open (durch vorherigen Sell), später Buy-to-cover
        var account = GetAccount();
        var instrument = GetInstrument();
        var openLots = new List<OpenLot>
        {
            CreateOpenLot(
                direction: PositionDirection.Short,
                originalQuantity: 100m,
                account: account,
                instrument: instrument,
                openUnitPrice: 200m,
                remainingOpenFees: 10m,
                remainingOpenTaxes: 4m)
        };

        var tradeEvent = CreateTradeEvent(
            side: TradeSide.Buy,
            account: account,
            instrument: instrument,
            quantity: 100m,
            unitPrice: 150m,
            fees: 12m,
            taxes: 4.8m
        );

        // Act
        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        // Assert
        //Erwartung: Short-Formel korrekt (Gewinn bei niedrigerem Rückkaufpreis).
        Assert.Single(result.Consumptions);
        var consumption = result.Consumptions[0];

        var grossPnL = (consumption.OpenUnitPrice.Amount
            - consumption.CloseUnitPrice.Amount) * consumption.MatchedQuantity.Value;
        var netPnL = grossPnL
            - consumption.AllocatedOpenFees.Amount
            - consumption.AllocatedOpenTaxes.Amount
            - consumption.AllocatedCloseFees.Amount
            - consumption.AllocatedCloseTaxes.Amount;

        Assert.Equal(5000m, grossPnL);
        Assert.Equal(4969.2m, netPnL);
        Assert.Equal(4969.2m, consumption.RealizedPnL.Amount);

    }

    private static Account GetAccount() => new Account(AccountId: AccountId.NewId(), AccountNumber: "12345");
    private static Instrument GetInstrument() => new Instrument(
        InstrumentId: InstrumentId.NewId(),
        ISIN: "US0378331005",
        Symbol: "AAPL",
        Name: "Apple Inc.",
        Teilfreistellungsquote: 0m);

    private static ExecutedTradeEvent GetTradeEvent() => new ExecutedTradeEvent(
           EventId: AccountEventId.NewId(),
           Account: GetAccount(),
           OccurredAt: DateTime.UtcNow,
           SourceBroker: "Source Broker",
           SourceReference: "Source Broker Reference",
           Instrument: GetInstrument(),
           Side: TradeSide.Buy,
           Quantity: new Quantity(100m),
           UnitPrice: new Money(100m, Currency.EUR),
           Fees: new Money(10m, Currency.EUR),
           Taxes: new Money(4m, Currency.EUR)
       );

    private static OpenLot CreateOpenLot(
        Account? account = null,
        Instrument? instrument = null,
        PositionDirection direction = PositionDirection.Long,
        decimal originalQuantity = 100m,
        decimal? remainingQuantity = null,
        decimal openUnitPrice = 100m,
        decimal remainingOpenFees = 10m,
        decimal remainingOpenTaxes = 4m,
        DateOnly? openTradeDate = null)
    {
        account = account ?? GetAccount();
        instrument = instrument ?? GetInstrument();
        return new OpenLot
        {
            LotId = new LotId(Guid.NewGuid()),
            Account = account,
            Instrument = instrument,
            OpenEvent = GetTradeEvent(),
            OpenTradeDate = openTradeDate ?? new DateOnly(2025, 1, 10),
            Direction = direction,
            OriginalQuantity = new Quantity(originalQuantity),
            RemainingQuantity = new Quantity(remainingQuantity ?? originalQuantity),
            OpenUnitPrice = new Money(openUnitPrice, Currency.EUR),
            RemainingOpenFees = new Money(remainingOpenFees, Currency.EUR),
            RemainingOpenTaxes = new Money(remainingOpenTaxes, Currency.EUR)
        };
    }

    private static ExecutedTradeEvent CreateTradeEvent(
        TradeSide side = TradeSide.Sell,
        decimal quantity = 100m,
        decimal unitPrice = 150m,
        decimal fees = 12m,
        decimal taxes = 4.8m,
        Account? account = null,
        Instrument? instrument = null,
        string sourceBroker = "BrokerX",
        string sourceReference = "REF-123",
        DateTime? occurredAt = null)
    {
        account = account ?? GetAccount();
        instrument = instrument ?? GetInstrument();
        return new ExecutedTradeEvent(
            EventId: AccountEventId.NewId(),
            Account: account,
            OccurredAt: occurredAt ?? DateTime.UtcNow,
            SourceBroker: sourceBroker,
            SourceReference: sourceReference,
            Instrument: instrument,
            Side: side,
            Quantity: new Quantity(quantity),
            UnitPrice: new Money(unitPrice, Currency.EUR),
            Fees: new Money(fees, Currency.EUR),
            Taxes: new Money(taxes, Currency.EUR)
        );
    }
}
