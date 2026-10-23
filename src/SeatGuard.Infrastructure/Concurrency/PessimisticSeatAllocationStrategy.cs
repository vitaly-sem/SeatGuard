using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Abstractions.Services;
using SeatGuard.Domain.Reservations;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Infrastructure.Concurrency;

/// <summary>
/// Serializes competing bookings on the seat row with SELECT ... FOR UPDATE.
/// The second request blocks until the first commits, then observes a held seat
/// and returns a deterministic conflict instead of retrying.
/// </summary>
internal sealed class PessimisticSeatAllocationStrategy(ISeatRepository seats) : ISeatAllocationStrategy
{
    public async Task<Seat?> AcquireAsync(
        SeatId seatId,
        ReservationId reservationId,
        CancellationToken cancellationToken = default)
    {
        var seat = await seats.FindForUpdateAsync(seatId, cancellationToken);
        seat?.Hold(reservationId);
        return seat;
    }
}
