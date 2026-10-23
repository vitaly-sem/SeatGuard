using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Seats;

public sealed class SeatUnavailableException(SeatId seatId, SeatStatus status)
    : DomainException($"Seat '{seatId}' is not available (current status: {status}).")
{
    public SeatId SeatId { get; } = seatId;

    public SeatStatus Status { get; } = status;
}
