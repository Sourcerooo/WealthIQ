using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Lot;

public sealed record TradeRealizationEntry(
    RealizationEntryId EntryId,
    AccountId AccountId,
    DateOnly RealizedOn,
    IReadOnlyList<EventSliceRef> SourceSlices,
    InstrumentId InstrumentId,
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
