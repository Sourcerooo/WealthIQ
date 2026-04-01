using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Ledger;

public abstract record PortfolioEntry
{
    protected PortfolioEntry(
        PortfolioEntryId entryId,
        AccountId accountId,
        DateTimeOffset occurredAt,
        DateOnly effectiveDate,
        PortfolioEntryCategory category,
        SourceProvenance sourceProvenance)
    {
        if (occurredAt == DateTimeOffset.MinValue)
        {
            throw new InvalidOperationException("OccurredAt must be set.");
        }

        ArgumentNullException.ThrowIfNull(sourceProvenance);
        EnsureNotWhiteSpace(sourceProvenance.SourceSystem, nameof(sourceProvenance.SourceSystem));
        EnsureNotWhiteSpace(sourceProvenance.ImportFormat, nameof(sourceProvenance.ImportFormat));
        EnsureNotWhiteSpace(sourceProvenance.SourceLocation, nameof(sourceProvenance.SourceLocation));
        EnsureNotWhiteSpace(sourceProvenance.SourceRecordReference, nameof(sourceProvenance.SourceRecordReference));

        EntryId = entryId;
        AccountId = accountId;
        OccurredAt = occurredAt;
        EffectiveDate = effectiveDate;
        Category = category;
        SourceProvenance = sourceProvenance;
    }

    public PortfolioEntryId EntryId { get; }
    public AccountId AccountId { get; }
    public DateTimeOffset OccurredAt { get; }
    public DateOnly EffectiveDate { get; }
    public PortfolioEntryCategory Category { get; }
    public SourceProvenance SourceProvenance { get; }

    protected static void EnsureNotWhiteSpace(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{paramName} must be provided.");
        }
    }

    protected static void EnsureNonNegative(Money value, string paramName)
    {
        if (value.Amount < 0m)
        {
            throw new InvalidOperationException($"{paramName} must be greater than or equal to zero.");
        }
    }
}
