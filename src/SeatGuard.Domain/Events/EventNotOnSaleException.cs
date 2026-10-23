using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Events;

public sealed class EventNotOnSaleException(EventId eventId, string reason)
    : DomainException($"Event '{eventId}' is not on sale: {reason}")
{
    public EventId EventId { get; } = eventId;
}
