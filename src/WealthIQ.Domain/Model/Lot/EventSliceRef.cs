using WealthIQ.Domain.Model.Event;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Lot;

public readonly record struct EventSliceRef(
    AccountEventId EventId,
    Quantity QuantityPortion
);
