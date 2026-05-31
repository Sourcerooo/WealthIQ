using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Ledger;

public sealed record AssetTransferEntry : PortfolioEntry
{
    public AssetTransferEntry(
        PortfolioEntryId entryId,
        AccountId accountId,
        DateTimeOffset occurredAt,
        DateOnly effectiveDate,
        SourceProvenance sourceProvenance,
        AssetTransferType transferType,
        InstrumentId? instrumentId = null,
        Quantity? quantity = null,
        Money? amount = null,
        string? counterpartyReference = null)
        : base(entryId, accountId, occurredAt, effectiveDate, PortfolioEntryCategory.AssetTransfer, sourceProvenance)
    {
        if (quantity is null && amount is null)
        {
            throw new InvalidOperationException("Asset transfer must contain either quantity or amount.");
        }

        if (quantity is { Value: <= 0m })
        {
            throw new InvalidOperationException("Asset transfer quantity must be greater than zero.");
        }

        if (amount is { Amount: <= 0m })
        {
            throw new InvalidOperationException("Asset transfer amount must be greater than zero.");
        }

        TransferType = transferType;
        InstrumentId = instrumentId;
        Quantity = quantity;
        Amount = amount;
        CounterpartyReference = counterpartyReference;
    }

    public AssetTransferType TransferType { get; }
    public InstrumentId? InstrumentId { get; }
    public Quantity? Quantity { get; }
    public Money? Amount { get; }
    public string? CounterpartyReference { get; }
}
