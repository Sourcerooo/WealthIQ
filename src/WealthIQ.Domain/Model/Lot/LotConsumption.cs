using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Event;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Lot;

public sealed record LotConsumption
{
    public required OpenLot OpenLot { get; init; }
    public required AccountEvent OpenEvent { get; init; }
    // Provenance
    public DateOnly OpenTradeDate { get; init; }
    public DateOnly CloseTradeDate { get; init; }
    public required Instrument Instrument { get; init; }
    public required Account Account { get; init; }
    public PositionDirection Direction { get; init; } // direction of the OPEN lot
    // Quantity slice closed in this match
    public Quantity MatchedQuantity { get; init; } // > 0
    public Money OpenUnitPrice { get; init; }
    public Money AllocatedOpenFees { get; init; }
    public Money AllocatedOpenTaxes { get; init; }
    public Money CloseUnitPrice { get; init; }
    public Money AllocatedCloseFees { get; init; }
    public Money AllocatedCloseTaxes { get; init; }
    // Derived metrics
    public Money CostBasis =>
        Direction switch
        {
            PositionDirection.Long => (OpenUnitPrice * MatchedQuantity.Value)
                + AllocatedOpenFees + AllocatedOpenTaxes,
            PositionDirection.Short => (CloseUnitPrice * MatchedQuantity.Value)
                + AllocatedCloseFees + AllocatedCloseTaxes,
            _ => throw new InvalidOperationException("Invalid position direction.")
        };

    public Money Proceeds =>
        Direction switch
        {
            PositionDirection.Long => (CloseUnitPrice * MatchedQuantity.Value)
                 - AllocatedCloseFees - AllocatedCloseTaxes,
            PositionDirection.Short => (OpenUnitPrice * MatchedQuantity.Value)
                 - AllocatedOpenFees - AllocatedOpenTaxes,
            _ => throw new InvalidOperationException("Invalid position direction.")
        };

    public Money RealizedPnL => Proceeds - CostBasis;
}
