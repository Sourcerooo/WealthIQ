
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Event;

public sealed record CashIncomeEvent(AccountEventId EventId,
    Account Account,
    DateTimeOffset OccurredAt,
    EventType Kind,
    string SourceBroker,
    string SourceReference,
    CashIncomeType IncomeType,
    Money GrossAmount,
    Money WithholdingTax,
    Money Fees)
    : AccountEvent(EventId,
    Account,
    OccurredAt,
    Kind,
    SourceBroker,
    SourceReference);