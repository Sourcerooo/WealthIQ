
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Event;

public sealed record CashIncomeEvent(AccountEventId EventId,
    Account Account,
    DateTimeOffset OccuredAt,
    EventType Kind,
    string SourceBroker,
    string SourceReference,
    CashIncomeType IncomeType,
    Money GrossAmount,
    Money WithholdingTax,
    Money Fees)
    : AccountEvent(EventId,
    Account,
    OccuredAt,
    Kind,
    SourceBroker,
    SourceReference);