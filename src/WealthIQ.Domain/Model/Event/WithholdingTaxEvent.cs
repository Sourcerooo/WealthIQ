using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Event;

public sealed record WithholdingTaxEvent(
    AccountEventId EventId,
    AccountId AccountId,
    DateTimeOffset OccurredAt,
    string SourceBroker,
    string SourceReference,
    InstrumentId InstrumentId,
    Money Amount)
    : AccountEvent(
        EventId,
        AccountId,
        OccurredAt,
        EventType.WithholdingTax,
        SourceBroker,
        SourceReference);
