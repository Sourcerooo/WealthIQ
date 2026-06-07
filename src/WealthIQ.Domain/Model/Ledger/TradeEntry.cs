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
        Money taxes,
        Money withheldTax = default)
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
        EnsureNonNegative(withheldTax, nameof(withheldTax));

        InstrumentId = instrumentId;
        Side = side;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Fees = fees;
        Taxes = taxes;
        WithheldTax = withheldTax.Amount == 0m ? new Money(0m, Currency.EUR) : withheldTax;
    }

    public InstrumentId InstrumentId { get; }
    public TradeSide Side { get; }
    public Quantity Quantity { get; }
    public Money UnitPrice { get; }
    public Money Fees { get; }
    public Money Taxes { get; }

    /// <summary>Capital-gains tax already withheld by the broker at sale (e.g. German KESt). Display/
    /// reconciliation only — NEVER part of FIFO proceeds/cost math. Default zero EUR.</summary>
    public Money WithheldTax { get; }
}
