using WealthIQ.Domain.Model.Lot;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Domain.Model.Matching;

public sealed record TradeMatchResult
{
    public required TradeEntry ClosingEntry { get; init; }
    public required IReadOnlyList<LotConsumption> Consumptions { get; init; }
    public required IReadOnlyList<OpenLot> UpdatedOpenLots { get; init; }
    public OpenLot? NewlyOpenedRemainderLot { get; init; }
}
