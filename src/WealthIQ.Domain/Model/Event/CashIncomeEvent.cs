
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Event;

public sealed record CashIncomeEvent(Guid EventId,
    AccountId AccountId,
    DateTimeOffset OccuredAt,
    EventType Kind,
    string SourceBroker,
    string SourceReference,
    CashIncomeType IncomeType,
    Money GrossAmount,
    Money WithholdingTax,
    Money Fees)
    : AccountEvent(EventId,
    AccountId,
    OccuredAt,
    Kind,
    SourceBroker,
    SourceReference);