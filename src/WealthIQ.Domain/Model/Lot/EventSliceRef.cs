using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Domain.Model.Lot;

public readonly record struct EventSliceRef(
    PortfolioEntryId EntryId,
    Quantity QuantityPortion
);
