using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Lot;

public sealed record TradeRealizationEntry(
    RealizationEntryId EntryId,
    Account AccountId,
    DateOnly RealizedOn,
    IReadOnlyList<EventSliceRef> SourceSlices,
    Instrument Instrument,
    Quantity ClosedQuantity,
    DateOnly OpenedOn,
    DateOnly ClosedOn,
    Money CostBasis,
    Money Proceeds,
    Money Fees,
    Money RealizedPnL
    )
    : RealizationEntry(
    EntryId,
    AccountId,
    RealizedOn,
    SourceSlices
    );
