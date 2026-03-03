using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Lot;

public sealed record CashRealizationEntry(
    RealizationEntryId EntryId,
    Account AccountId,
    DateOnly RealizedOn,
    IReadOnlyList<EventSliceRef> SourceSlices,
    CashIncomeType IncomeType,
    Money GrossAmount,
    Money NetAmount
    )
    : RealizationEntry(
    EntryId,
    AccountId,
    RealizedOn,
    SourceSlices
    );
