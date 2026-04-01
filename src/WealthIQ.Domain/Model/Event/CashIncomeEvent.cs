
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Event;

public sealed record CashIncomeEvent(AccountEventId EventId,
    AccountId AccountId,
    DateTimeOffset OccurredAt,
    EventType Kind,
    string SourceBroker,
    string SourceReference,
    InstrumentId InstrumentId,
    CashIncomeType IncomeType,
    Money GrossAmount,
    Money WithholdingTax,
    Money Fees)
    : AccountEvent(EventId,
    AccountId,
    OccurredAt,
    Kind,
    SourceBroker,
    SourceReference);
