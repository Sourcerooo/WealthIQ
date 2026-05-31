using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Ledger;

public sealed record PositionAdjustmentEntry : PortfolioEntry
{
    public PositionAdjustmentEntry(
        PortfolioEntryId entryId,
        AccountId accountId,
        DateTimeOffset occurredAt,
        DateOnly effectiveDate,
        SourceProvenance sourceProvenance,
        InstrumentId instrumentId,
        PositionAdjustmentType adjustmentType,
        Quantity quantityDelta,
        string reason,
        Money? amountDelta = null)
        : base(entryId, accountId, occurredAt, effectiveDate, PortfolioEntryCategory.PositionAdjustment, sourceProvenance)
    {
        if (quantityDelta.Value == 0m)
        {
            throw new InvalidOperationException("Position adjustment quantity delta must not be zero.");
        }

        EnsureNotWhiteSpace(reason, nameof(reason));

        if (amountDelta is not null)
        {
            EnsureNonNegative(amountDelta.Value with { Amount = Math.Abs(amountDelta.Value.Amount) }, nameof(amountDelta));
        }

        InstrumentId = instrumentId;
        AdjustmentType = adjustmentType;
        QuantityDelta = quantityDelta;
        AmountDelta = amountDelta;
        Reason = reason;
    }

    public InstrumentId InstrumentId { get; }
    public PositionAdjustmentType AdjustmentType { get; }
    public Quantity QuantityDelta { get; }
    public Money? AmountDelta { get; }
    public string Reason { get; }
}
