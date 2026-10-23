using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Seats;

public sealed class InvalidSeatStateException(SeatId seatId, string message)
    : DomainException($"Seat '{seatId}': {message}")
{
    public SeatId SeatId { get; } = seatId;
}
