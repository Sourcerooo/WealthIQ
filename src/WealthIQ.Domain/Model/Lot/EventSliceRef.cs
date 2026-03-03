using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Lot;

public readonly record struct EventSliceRef(
    Guid EventId,
    Quantity QuantityPortion
);
