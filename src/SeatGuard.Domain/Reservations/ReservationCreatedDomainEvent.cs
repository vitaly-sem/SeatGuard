using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Reservations;

public sealed record ReservationCreatedDomainEvent(
    ReservationId ReservationId,
    Events.EventId EventId,
    Seats.SeatId SeatId,
    CustomerId CustomerId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset OccurredAt) : IDomainEvent;
