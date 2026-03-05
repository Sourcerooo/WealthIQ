using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Event;

public sealed record ExecutedTradeEvent(
    AccountEventId EventId,
    AccountId AccountId,
    DateTimeOffset OccurredAt,
    string SourceBroker,
    string SourceReference,
    InstrumentId InstrumentId,
    TradeSide Side,
    Quantity Quantity,
    Money UnitPrice,
    Money Fees,
    Money Taxes)
    : AccountEvent(EventId,
    AccountId,
    OccurredAt,
    EventType.ExecutedTrade,
    SourceBroker,
    SourceReference);
