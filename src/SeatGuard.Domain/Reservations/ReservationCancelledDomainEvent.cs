using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Reservations;

public sealed record ReservationCancelledDomainEvent(
    ReservationId ReservationId,
    Seats.SeatId SeatId,
    DateTimeOffset OccurredAt) : IDomainEvent;
