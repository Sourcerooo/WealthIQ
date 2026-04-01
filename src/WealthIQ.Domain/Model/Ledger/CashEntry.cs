using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Ledger;

public sealed record CashEntry : PortfolioEntry
{
    public CashEntry(
        PortfolioEntryId entryId,
        AccountId accountId,
        DateTimeOffset occurredAt,
        DateOnly effectiveDate,
        SourceProvenance sourceProvenance,
        InstrumentId cashInstrumentId,
        CashFlowType cashFlowType,
        Money grossAmount,
        Money fees,
        Money taxes,
        InstrumentId? relatedInstrumentId = null)
        : base(entryId, accountId, occurredAt, effectiveDate, PortfolioEntryCategory.Cash, sourceProvenance)
    {
        if (grossAmount.Amount == 0m)
        {
            throw new InvalidOperationException("Cash gross amount must not be zero.");
        }

        EnsureNonNegative(fees, nameof(fees));
        EnsureNonNegative(taxes, nameof(taxes));

        CashInstrumentId = cashInstrumentId;
        CashFlowType = cashFlowType;
        GrossAmount = grossAmount;
        Fees = fees;
        Taxes = taxes;
        RelatedInstrumentId = relatedInstrumentId;
    }

    public InstrumentId CashInstrumentId { get; }
    public CashFlowType CashFlowType { get; }
    public Money GrossAmount { get; }
    public Money Fees { get; }
    public Money Taxes { get; }
    public InstrumentId? RelatedInstrumentId { get; }
}
