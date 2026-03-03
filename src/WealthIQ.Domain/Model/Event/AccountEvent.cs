using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Event;
public abstract record AccountEvent(
    Guid EventId,
    AccountId AccountId,
    DateTimeOffset OccuredAt,
    EventType Kind,
    string SourceBroker,
    string SourceReference
    );