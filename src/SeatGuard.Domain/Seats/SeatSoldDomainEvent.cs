using SeatGuard.Domain.Common;
using SeatGuard.Domain.Reservations;

namespace SeatGuard.Domain.Seats;

public sealed record SeatSoldDomainEvent(
    SeatId SeatId,
    ReservationId ReservationId,
    DateTimeOffset OccurredAt) : IDomainEvent;
