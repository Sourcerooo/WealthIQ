using WealthIQ.Domain.Model.Event;
using WealthIQ.Domain.Model.Lot;

namespace WealthIQ.Domain.Model.Matching;

public sealed record TradeMatchResult
{
    public required ExecutedTradeEvent ClosingEvent { get; init; }
    public required IReadOnlyList<LotConsumption> Consumptions { get; init; }
    public required IReadOnlyList<OpenLot> UpdatedOpenLots { get; init; }
    public OpenLot? NewlyOpenedRemainderLot { get; init; }
}
