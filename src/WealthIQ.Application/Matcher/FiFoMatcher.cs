using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Interface.Matcher;
using WealthIQ.Domain.Model.Event;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Lot;
using WealthIQ.Domain.Model.Matching;

namespace WealthIQ.Application.Matcher;

public sealed record FiFoMatcher : ILotMatcher
{
    public TradeMatchResult Match(
        ExecutedTradeEvent tradeEvent,
        IReadOnlyList<OpenLot> currentOpenLots,
        LotMatchingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(tradeEvent);

        var oppositeDirection = tradeEvent.Side == TradeSide.Buy ? PositionDirection.Short : PositionDirection.Long;
        var remainingQuantityToMatch = tradeEvent.Quantity.Value;
        var updateOpenLots = currentOpenLots.ToList();
        updateOpenLots.Sort((x, y) => x.OpenTradeDate.CompareTo(y.OpenTradeDate));
        var consumptionList = new List<LotConsumption>();
        var newOpenLot = default(OpenLot?);
        while (remainingQuantityToMatch > 0m)
        {
            var lotIndex = updateOpenLots.FindIndex(openLot =>
            openLot.AccountId == tradeEvent.AccountId
                && openLot.InstrumentId == tradeEvent.InstrumentId
                && openLot.Direction == oppositeDirection
                && openLot.RemainingQuantity.Value > 0);
            if (lotIndex < 0)
            {
                break;
            }
            var openLot = updateOpenLots[lotIndex];
            var quantityToMatch = new Quantity(Math.Min(openLot.RemainingQuantity.Value, remainingQuantityToMatch));
            remainingQuantityToMatch -= quantityToMatch.Value;
            var ratio = quantityToMatch.Value / tradeEvent.Quantity.Value;
            var changedOpenLot = openLot.Consume(quantityToMatch);
            consumptionList.Add(
                 new LotConsumption
                 {
                     LotId = openLot.LotId,
                     OpenEventId = openLot.OpenEventId,
                     OpenTradeDate = openLot.OpenTradeDate,
                     CloseTradeDate = DateOnly.FromDateTime(tradeEvent.OccuredAt.DateTime),
                     InstrumentId = openLot.InstrumentId,
                     Direction = openLot.Direction,
                     MatchedQuantity = quantityToMatch,
                     OpenUnitPrice = openLot.OpenUnitPrice,
                     AllocatedOpenFees = openLot.RemainingOpenFees - changedOpenLot.RemainingOpenFees,
                     AllocatedOpenTaxes = openLot.RemainingOpenTaxes - changedOpenLot.RemainingOpenTaxes,
                     CloseUnitPrice = tradeEvent.UnitPrice,
                     AllocatedCloseFees = tradeEvent.Fees * ratio,
                     AllocatedCloseTaxes = tradeEvent.Taxes * ratio
                 }
             );
            updateOpenLots[lotIndex] = changedOpenLot;
        }

        if (remainingQuantityToMatch > 0)
        {
            var ratio = remainingQuantityToMatch / tradeEvent.Quantity.Value;
            newOpenLot = new OpenLot
            {
                LotId = new LotId(Guid.NewGuid()),
                AccountId = tradeEvent.AccountId,
                InstrumentId = tradeEvent.InstrumentId,
                OpenEventId = tradeEvent.EventId,
                OpenTradeDate = DateOnly.FromDateTime(tradeEvent.OccuredAt.DateTime),
                Direction = tradeEvent.Side == TradeSide.Buy ? PositionDirection.Long : PositionDirection.Short,
                OriginalQuantity = new Quantity(remainingQuantityToMatch),
                RemainingQuantity = new Quantity(remainingQuantityToMatch),
                OpenUnitPrice = tradeEvent.UnitPrice,
                RemainingOpenFees = tradeEvent.Fees * ratio,
                RemainingOpenTaxes = tradeEvent.Taxes * ratio
            };
        }

        var result = new TradeMatchResult
        {
            ClosingEvent = tradeEvent,
            Consumptions = consumptionList,
            UpdatedOpenLots = updateOpenLots,
            NewlyOpenedRemainderLot = newOpenLot
        };

        return result;
    }
}
