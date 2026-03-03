using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Event;

public sealed record ExecutedTradeEvent(
    AccountEventId EventId,
    Account Account,
    DateTimeOffset OccuredAt,
    string SourceBroker,
    string SourceReference,
    Instrument Instrument,
    TradeSide Side,
    Quantity Quantity,
    Money UnitPrice,
    Money Fees,
    Money Taxes)
    : AccountEvent(EventId,
    Account,
    OccuredAt,
    EventType.ExecutedTrade,
    SourceBroker,
    SourceReference);
