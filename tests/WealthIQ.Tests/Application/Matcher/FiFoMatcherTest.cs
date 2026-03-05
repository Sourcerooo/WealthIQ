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
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var openLots = new List<OpenLot> { CreateOpenLot(accountId: accountId, instrumentId: instrumentId) };
        var tradeEvent = CreateTradeEvent(accountId: accountId, instrumentId: instrumentId);

        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

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
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var openLots = new List<OpenLot> { CreateOpenLot(accountId: accountId, instrumentId: instrumentId) };

        var tradeEvent = CreateTradeEvent(
            accountId: accountId,
            instrumentId: instrumentId,
            quantity: 40m,
            unitPrice: 200m,
            fees: 20m,
            taxes: 8m);

        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

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
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var olderLotId = LotId.NewId();
        var newerLotId = LotId.NewId();

        var openLots = new List<OpenLot>
        {
            CreateOpenLot(
                lotId: newerLotId,
                originalQuantity: 200m,
                accountId: accountId,
                instrumentId: instrumentId,
                openTradeDate: new DateOnly(2025, 1, 10)),
            CreateOpenLot(
                lotId: olderLotId,
                accountId: accountId,
                instrumentId: instrumentId,
                openTradeDate: new DateOnly(2025, 1, 9),
                openUnitPrice: 50m,
                remainingOpenFees: 8m,
                remainingOpenTaxes: 3m)
        };

        var tradeEvent = CreateTradeEvent(
            accountId: accountId,
            instrumentId: instrumentId,
            quantity: 150m,
            unitPrice: 200m,
            fees: 21m,
            taxes: 9m);

        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        Assert.Null(result.NewlyOpenedRemainderLot);
        Assert.Equal(2, result.Consumptions.Count);

        var first = result.Consumptions[0];
        Assert.Equal(olderLotId, first.OpenLotId);
        Assert.Equal(100m, first.MatchedQuantity.Value);
        Assert.Equal(8m, first.AllocatedOpenFees.Amount);
        Assert.Equal(3m, first.AllocatedOpenTaxes.Amount);
        Assert.Equal(14m, decimal.Round(first.AllocatedCloseFees.Amount, 2));
        Assert.Equal(6m, decimal.Round(first.AllocatedCloseTaxes.Amount, 2));

        var second = result.Consumptions[1];
        Assert.Equal(newerLotId, second.OpenLotId);
        Assert.Equal(50m, second.MatchedQuantity.Value);
        Assert.Equal(2.5m, second.AllocatedOpenFees.Amount);
        Assert.Equal(1m, second.AllocatedOpenTaxes.Amount);
        Assert.Equal(7m, decimal.Round(second.AllocatedCloseFees.Amount, 2));
        Assert.Equal(3m, decimal.Round(second.AllocatedCloseTaxes.Amount, 2));

        var olderUpdated = result.UpdatedOpenLots.Single(x => x.LotId == olderLotId);
        Assert.Equal(0m, olderUpdated.RemainingQuantity.Value);
        var newerUpdated = result.UpdatedOpenLots.Single(x => x.LotId == newerLotId);
        Assert.Equal(150m, newerUpdated.RemainingQuantity.Value);
    }

    [Fact]
    public void Match_SellWithoutOpenLong_CreatesNewShortOpenLot()
    {
        var openLots = new List<OpenLot>();
        var tradeEvent = CreateTradeEvent(unitPrice: 200m, fees: 10m, taxes: 4m);

        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        Assert.Empty(result.Consumptions);
        Assert.NotNull(result.NewlyOpenedRemainderLot);
        var openLot = result.NewlyOpenedRemainderLot;
        Assert.Equal(PositionDirection.Short, openLot!.Direction);
        Assert.Equal(100m, openLot.RemainingQuantity.Value);
    }

    [Fact]
    public void Match_BuyWithoutOpenShort_CreatesNewLongOpenLot()
    {
        var openLots = new List<OpenLot>();
        var tradeEvent = CreateTradeEvent(side: TradeSide.Buy, unitPrice: 200m, fees: 10m, taxes: 4m);

        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        Assert.Empty(result.Consumptions);
        Assert.NotNull(result.NewlyOpenedRemainderLot);
        var openLot = result.NewlyOpenedRemainderLot;
        Assert.Equal(PositionDirection.Long, openLot!.Direction);
        Assert.Equal(100m, openLot.RemainingQuantity.Value);
    }

    [Fact]
    public void Match_OverCloseLong_ClosesExistingAndOpensShortRemainder()
    {
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var openLots = new List<OpenLot>
        {
            CreateOpenLot(originalQuantity: 50m, accountId: accountId, instrumentId: instrumentId)
        };

        var tradeEvent = CreateTradeEvent(
            side: TradeSide.Sell,
            accountId: accountId,
            instrumentId: instrumentId,
            quantity: 80m,
            unitPrice: 200m,
            fees: 8m,
            taxes: 4m);

        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        Assert.Single(result.Consumptions);
        Assert.Equal(50m, result.Consumptions[0].MatchedQuantity.Value);

        var updatedOpenLot = result.UpdatedOpenLots.Single();
        Assert.Equal(0m, updatedOpenLot.RemainingQuantity.Value);

        Assert.NotNull(result.NewlyOpenedRemainderLot);
        var remainderLot = result.NewlyOpenedRemainderLot;
        Assert.Equal(PositionDirection.Short, remainderLot!.Direction);
        Assert.Equal(30m, remainderLot.OriginalQuantity.Value);
        Assert.Equal(30m, remainderLot.RemainingQuantity.Value);
        Assert.Equal(3m, remainderLot.RemainingOpenFees.Amount);
        Assert.Equal(1.5m, remainderLot.RemainingOpenTaxes.Amount);
    }

    [Fact]
    public void Match_IgnoresLotsFromDifferentAccountOrInstrument()
    {
        var tradeAccountId = AccountId.NewId();
        var tradeInstrumentId = InstrumentId.NewId();
        var differentAccountId = AccountId.NewId();
        var differentInstrumentId = InstrumentId.NewId();

        var differentAccountLot = CreateOpenLot(accountId: differentAccountId, instrumentId: tradeInstrumentId, originalQuantity: 70m);
        var differentInstrumentLot = CreateOpenLot(accountId: tradeAccountId, instrumentId: differentInstrumentId, originalQuantity: 80m);
        var matchingLot = CreateOpenLot(accountId: tradeAccountId, instrumentId: tradeInstrumentId, originalQuantity: 60m);
        var openLots = new List<OpenLot> { differentAccountLot, differentInstrumentLot, matchingLot };

        var tradeEvent = CreateTradeEvent(accountId: tradeAccountId, instrumentId: tradeInstrumentId, quantity: 50m);

        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        Assert.Single(result.Consumptions);
        var consumption = result.Consumptions[0];
        Assert.Equal(matchingLot.LotId, consumption.OpenLotId);
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
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var openLots = new List<OpenLot>
        {
            CreateOpenLot(
                direction: PositionDirection.Long,
                accountId: accountId,
                instrumentId: instrumentId,
                originalQuantity: 100m,
                openUnitPrice: 100m,
                remainingOpenFees: 10m,
                remainingOpenTaxes: 4m)
        };

        var tradeEvent = CreateTradeEvent(
            side: TradeSide.Sell,
            accountId: accountId,
            instrumentId: instrumentId,
            quantity: 100m,
            unitPrice: 150m,
            fees: 12m,
            taxes: 4.8m);

        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        Assert.Single(result.Consumptions);
        var consumption = result.Consumptions[0];

        var grossPnL = (consumption.CloseUnitPrice.Amount - consumption.OpenUnitPrice.Amount) * consumption.MatchedQuantity.Value;
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
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var openLots = new List<OpenLot>
        {
            CreateOpenLot(
                direction: PositionDirection.Short,
                originalQuantity: 100m,
                accountId: accountId,
                instrumentId: instrumentId,
                openUnitPrice: 200m,
                remainingOpenFees: 10m,
                remainingOpenTaxes: 4m)
        };

        var tradeEvent = CreateTradeEvent(
            side: TradeSide.Buy,
            accountId: accountId,
            instrumentId: instrumentId,
            quantity: 100m,
            unitPrice: 150m,
            fees: 12m,
            taxes: 4.8m);

        var fifoMatcher = new FiFoMatcher();
        var result = fifoMatcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);

        Assert.Single(result.Consumptions);
        var consumption = result.Consumptions[0];

        var grossPnL = (consumption.OpenUnitPrice.Amount - consumption.CloseUnitPrice.Amount) * consumption.MatchedQuantity.Value;
        var netPnL = grossPnL
            - consumption.AllocatedOpenFees.Amount
            - consumption.AllocatedOpenTaxes.Amount
            - consumption.AllocatedCloseFees.Amount
            - consumption.AllocatedCloseTaxes.Amount;

        Assert.Equal(5000m, grossPnL);
        Assert.Equal(4969.2m, netPnL);
        Assert.Equal(4969.2m, consumption.RealizedPnL.Amount);
    }

    private static OpenLot CreateOpenLot(
        LotId? lotId = null,
        AccountId? accountId = null,
        InstrumentId? instrumentId = null,
        PositionDirection direction = PositionDirection.Long,
        decimal originalQuantity = 100m,
        decimal? remainingQuantity = null,
        decimal openUnitPrice = 100m,
        decimal remainingOpenFees = 10m,
        decimal remainingOpenTaxes = 4m,
        DateOnly? openTradeDate = null)
    {
        var effectiveAccountId = accountId ?? AccountId.NewId();
        var effectiveInstrumentId = instrumentId ?? InstrumentId.NewId();

        return new OpenLot
        {
            LotId = lotId ?? LotId.NewId(),
            AccountId = effectiveAccountId,
            InstrumentId = effectiveInstrumentId,
            OpenEventId = AccountEventId.NewId(),
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
        AccountId? accountId = null,
        InstrumentId? instrumentId = null,
        string sourceBroker = "BrokerX",
        string sourceReference = "REF-123",
        DateTimeOffset? occurredAt = null)
    {
        var effectiveAccountId = accountId ?? AccountId.NewId();
        var effectiveInstrumentId = instrumentId ?? InstrumentId.NewId();

        return new ExecutedTradeEvent(
            EventId: AccountEventId.NewId(),
            AccountId: effectiveAccountId,
            OccurredAt: occurredAt ?? DateTimeOffset.UtcNow,
            SourceBroker: sourceBroker,
            SourceReference: sourceReference,
            InstrumentId: effectiveInstrumentId,
            Side: side,
            Quantity: new Quantity(quantity),
            UnitPrice: new Money(unitPrice, Currency.EUR),
            Fees: new Money(fees, Currency.EUR),
            Taxes: new Money(taxes, Currency.EUR));
    }
}
