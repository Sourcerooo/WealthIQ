using System;
using System.Collections.Generic;
using System.Text;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Lot;

public sealed record LotConsumption
{
    public LotId LotId { get; init; }
    public Guid OpenEventId { get; init; }
    // Provenance
    public DateOnly OpenTradeDate { get; init; }
    public DateOnly CloseTradeDate { get; init; }
    public InstrumentId InstrumentId { get; init; }
    public AccountId AccountId { get; init; }
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
        (OpenUnitPrice * MatchedQuantity.Value)
        + AllocatedOpenFees + AllocatedOpenTaxes;
    public Money Proceeds =>
        (CloseUnitPrice * MatchedQuantity.Value)
        - AllocatedCloseFees - AllocatedCloseTaxes;
    public Money RealizedPnL => Proceeds - CostBasis;
}
