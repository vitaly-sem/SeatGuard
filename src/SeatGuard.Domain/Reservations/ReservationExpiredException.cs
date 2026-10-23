using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Reservations;

public sealed class ReservationExpiredException(ReservationId reservationId, DateTimeOffset expiresAt)
    : DomainException($"Reservation '{reservationId}' expired at {expiresAt:O}.")
{
    public ReservationId ReservationId { get; } = reservationId;

    public DateTimeOffset ExpiresAt { get; } = expiresAt;
}
