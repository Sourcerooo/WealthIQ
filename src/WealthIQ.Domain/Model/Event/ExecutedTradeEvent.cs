using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Event;

public sealed record ExecutedTradeEvent(
    Guid EventId,
    AccountId AccountId,
    DateTimeOffset OccuredAt,
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
    OccuredAt,
    EventType.ExecutedTrade,
    SourceBroker,
    SourceReference);
