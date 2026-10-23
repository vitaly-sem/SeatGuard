using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Events;

public sealed record EventCreatedDomainEvent(
    EventId EventId,
    string Name,
    int SeatCount,
    DateTimeOffset OccurredAt) : IDomainEvent;
