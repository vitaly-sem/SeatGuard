using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Abstractions.Services;
using SeatGuard.Domain.Reservations;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Infrastructure.Concurrency;

/// <summary>
/// Reads the seat without locking and lets the database reject the write if the
/// row version (xmin) changed in the meantime. The resulting
/// DbUpdateConcurrencyException is translated into a conflict by the unit of work.
/// No retry is attempted: for a booking, "the seat is taken" is a valid outcome,
/// not a transient error.
/// </summary>
internal sealed class OptimisticSeatAllocationStrategy(ISeatRepository seats) : ISeatAllocationStrategy
{
    public async Task<Seat?> AcquireAsync(
        SeatId seatId,
        ReservationId reservationId,
        CancellationToken cancellationToken = default)
    {
        var seat = await seats.FindAsync(seatId, cancellationToken);
        seat?.Hold(reservationId);
        return seat;
    }
}
