using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Reservations;

public sealed record ReservationConfirmedDomainEvent(
    ReservationId ReservationId,
    Seats.SeatId SeatId,
    string TicketCode,
    DateTimeOffset OccurredAt) : IDomainEvent;
