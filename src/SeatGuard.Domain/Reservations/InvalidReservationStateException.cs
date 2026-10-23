using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Reservations;

public sealed class InvalidReservationStateException(
    ReservationId reservationId,
    ReservationStatus status,
    string message)
    : DomainException($"Reservation '{reservationId}' is {status}: {message}")
{
    public ReservationId ReservationId { get; } = reservationId;

    public ReservationStatus Status { get; } = status;
}
