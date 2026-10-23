using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Reservations;

public sealed record ReservationExpiredDomainEvent(
    ReservationId ReservationId,
    Seats.SeatId SeatId,
    DateTimeOffset OccurredAt) : IDomainEvent;
