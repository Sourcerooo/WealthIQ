using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Interface.Matcher;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Lot;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Domain.Model.Matching;

namespace WealthIQ.Application.Matcher;

public sealed record FiFoMatcher : ILotMatcher
{
    public TradeMatchResult Match(
        TradeEntry tradeEntry,
        IReadOnlyList<OpenLot> currentOpenLots,
        LotMatchingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(tradeEntry);

        var oppositeDirection = tradeEntry.Side == TradeSide.Buy ? PositionDirection.Short : PositionDirection.Long;
        var remainingQuantityToMatch = tradeEntry.Quantity.Value;
        var updateOpenLots = currentOpenLots.ToList();
        updateOpenLots.Sort((x, y) => x.OpenOccurredAt.CompareTo(y.OpenOccurredAt));
        var consumptionList = new List<LotConsumption>();
        var newOpenLot = default(OpenLot?);
        while (remainingQuantityToMatch > 0m)
        {
            var lotIndex = updateOpenLots.FindIndex(openLot =>
            openLot.AccountId == tradeEntry.AccountId
                && openLot.InstrumentId == tradeEntry.InstrumentId
                && openLot.Direction == oppositeDirection
                && openLot.RemainingQuantity.Value > 0);
            if (lotIndex < 0)
            {
                break;
            }
            var openLot = updateOpenLots[lotIndex];
            var quantityToMatch = new Quantity(Math.Min(openLot.RemainingQuantity.Value, remainingQuantityToMatch));
            remainingQuantityToMatch -= quantityToMatch.Value;
            var ratio = quantityToMatch.Value / tradeEntry.Quantity.Value;
            var changedOpenLot = openLot.Consume(quantityToMatch);
            consumptionList.Add(
                 new LotConsumption
                 {
                     OpenLotId = openLot.LotId,
                     OpenEntryId = openLot.OpenEntryId,
                     OpenTradeDate = openLot.OpenTradeDate,
                     CloseTradeDate = DateOnly.FromDateTime(tradeEntry.OccurredAt.DateTime),
                     InstrumentId = openLot.InstrumentId,
                     AccountId = openLot.AccountId,
                     Direction = openLot.Direction,
                     MatchedQuantity = quantityToMatch,
                     OpenUnitPrice = openLot.OpenUnitPrice,
                     AllocatedOpenFees = openLot.RemainingOpenFees - changedOpenLot.RemainingOpenFees,
                     AllocatedOpenTaxes = openLot.RemainingOpenTaxes - changedOpenLot.RemainingOpenTaxes,
                     CloseUnitPrice = tradeEntry.UnitPrice,
                     AllocatedCloseFees = tradeEntry.Fees * ratio,
                     AllocatedCloseTaxes = tradeEntry.Taxes * ratio
                 }
             );
            updateOpenLots[lotIndex] = changedOpenLot;
        }

        if (remainingQuantityToMatch > 0)
        {
            var ratio = remainingQuantityToMatch / tradeEntry.Quantity.Value;
            newOpenLot = new OpenLot
            {
                LotId = LotId.NewId(),
                AccountId = tradeEntry.AccountId,
                InstrumentId = tradeEntry.InstrumentId,
                OpenEntryId = tradeEntry.EntryId,
                OpenOccurredAt = tradeEntry.OccurredAt,
                OpenTradeDate = DateOnly.FromDateTime(tradeEntry.OccurredAt.DateTime),
                Direction = tradeEntry.Side == TradeSide.Buy ? PositionDirection.Long : PositionDirection.Short,
                OriginalQuantity = new Quantity(remainingQuantityToMatch),
                RemainingQuantity = new Quantity(remainingQuantityToMatch),
                OpenUnitPrice = tradeEntry.UnitPrice,
                RemainingOpenFees = tradeEntry.Fees * ratio,
                RemainingOpenTaxes = tradeEntry.Taxes * ratio
            };
        }

        var result = new TradeMatchResult
        {
            ClosingEntry = tradeEntry,
            Consumptions = consumptionList,
            UpdatedOpenLots = updateOpenLots,
            NewlyOpenedRemainderLot = newOpenLot
        };

        return result;
    }
}
