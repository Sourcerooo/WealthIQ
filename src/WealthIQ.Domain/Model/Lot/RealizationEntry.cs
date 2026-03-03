using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Lot;

public abstract record RealizationEntry(
    Guid EntryId,
    AccountId AccountId,
    DateOnly RealizedOn,
    IReadOnlyList<EventSliceRef> SourceSlices
    );
