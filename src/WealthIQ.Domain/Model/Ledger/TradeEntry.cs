using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Ledger;

public sealed record TradeEntry : PortfolioEntry
{
    public TradeEntry(
        PortfolioEntryId entryId,
        AccountId accountId,
        DateTimeOffset occurredAt,
        DateOnly effectiveDate,
        SourceProvenance sourceProvenance,
        InstrumentId instrumentId,
        TradeSide side,
        Quantity quantity,
        Money unitPrice,
        Money fees,
        Money taxes)
        : base(entryId, accountId, occurredAt, effectiveDate, PortfolioEntryCategory.Trade, sourceProvenance)
    {
        if (quantity.Value <= 0m)
        {
            throw new InvalidOperationException("Trade quantity must be greater than zero.");
        }

        if (unitPrice.Amount <= 0m)
        {
            throw new InvalidOperationException("Trade unit price must be greater than zero.");
        }

        EnsureNonNegative(fees, nameof(fees));
        EnsureNonNegative(taxes, nameof(taxes));

        InstrumentId = instrumentId;
        Side = side;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Fees = fees;
        Taxes = taxes;
    }

    public InstrumentId InstrumentId { get; }
    public TradeSide Side { get; }
    public Quantity Quantity { get; }
    public Money UnitPrice { get; }
    public Money Fees { get; }
    public Money Taxes { get; }
}
