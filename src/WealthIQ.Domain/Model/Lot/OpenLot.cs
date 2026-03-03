using System;
using System.Collections.Generic;
using System.Text;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Lot;

public sealed record OpenLot
{
    public LotId LotId { get; init; }
    public AccountId AccountId { get; init; }
    public InstrumentId InstrumentId { get; init; }

    //Lot Identity / provenance
    public Guid OpenEventId {  get; init; }
    public DateOnly OpenTradeDate { get; init; }

    public PositionDirection Direction { get; init; }
    public Quantity OriginalQuantity { get; init; }
    public Quantity RemainingQuantity { get; init; }
    public Money OpenUnitPrice { get; init; }
    public Money RemainingOpenFees { get; init; }
    public Money RemainingOpenTaxes { get; init; }
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
            RemainingOpenTaxes = new Money(RemainingOpenTaxes.Amount * (1m - ratio), RemainingOpenTaxes.Currency)
        };
    }
}
