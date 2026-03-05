using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Event;

public readonly record struct AccountEventId(Guid Value)
{
    public override string ToString() => Value.ToString();
    public static AccountEventId NewId() => new AccountEventId(Guid.NewGuid());
    public static explicit operator AccountEventId(Guid value) => new AccountEventId(value);
};

public abstract record AccountEvent(
    AccountEventId EventId,
    AccountId AccountId,
    DateTimeOffset OccurredAt,
    EventType Kind,
    string SourceBroker,
    string SourceReference
    );
