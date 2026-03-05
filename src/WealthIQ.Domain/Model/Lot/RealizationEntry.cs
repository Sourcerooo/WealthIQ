using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Lot;

public readonly record struct RealizationEntryId(Guid Value)
{
    public override string ToString() => Value.ToString();
    public static RealizationEntryId NewId() => new RealizationEntryId(Guid.NewGuid());
    public static explicit operator RealizationEntryId(Guid value) => new RealizationEntryId(value);
};

public abstract record RealizationEntry(
    RealizationEntryId EntryId,
    AccountId AccountId,
    DateOnly RealizedOn,
    IReadOnlyList<EventSliceRef> SourceSlices
    );
