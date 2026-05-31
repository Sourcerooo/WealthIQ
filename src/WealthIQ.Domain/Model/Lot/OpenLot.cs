using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Domain.Model.Lot;

public sealed record OpenLot
{
    public LotId LotId { get; init; }
    public required AccountId AccountId { get; init; }
    public required InstrumentId InstrumentId { get; init; }

    //Lot Identity / provenance
    public required PortfolioEntryId OpenEntryId { get; init; }
    public DateTimeOffset OpenOccurredAt { get; init; }
    public DateOnly OpenTradeDate { get; init; }

    /// <summary>The opening trade's source record reference (e.g. broker transaction id). Used as the
    /// deterministic FIFO tie-break for lots that share <see cref="OpenOccurredAt"/>, mirroring
    /// <c>PortfolioLedger</c> ordering. Defaults to empty for lots built without provenance.</summary>
    public string OpenSourceReference { get; init; } = "";

    public PositionDirection Direction { get; init; }
    public Quantity OriginalQuantity { get; init; }
    public Quantity RemainingQuantity { get; init; }
    public Money OpenUnitPrice { get; init; }
    public Money RemainingOpenFees { get; init; }
    public Money RemainingOpenTaxes { get; init; }
    public Money AccumulatedVorabpauschale { get; init; } = new(0m, Currency.EUR);
    public bool IsClosed => RemainingQuantity.Value == 0;
    public OpenLot Consume(Quantity quantityToClose)
    {
        if (quantityToClose.Value <= 0)
        {
            throw new InvalidOperationException("Quantity to close must be greater than zero.");
        }
        if (quantityToClose.Value > RemainingQuantity.Value)
        {
            throw new InvalidOperationException($"Cannot consume more quantity than remaining in the lot. Attempted to consume {quantityToClose.Value} but only {RemainingQuantity.Value} is available.");
        }
        var ratio = quantityToClose.Value / RemainingQuantity.Value;
        return this with
        {
            RemainingQuantity = new Quantity(RemainingQuantity.Value - quantityToClose.Value),
            RemainingOpenFees = new Money(RemainingOpenFees.Amount * (1m - ratio), RemainingOpenFees.Currency),
            RemainingOpenTaxes = new Money(RemainingOpenTaxes.Amount * (1m - ratio), RemainingOpenTaxes.Currency),
            AccumulatedVorabpauschale = new Money(AccumulatedVorabpauschale.Amount * (1m - ratio), AccumulatedVorabpauschale.Currency)
        };
    }
}
