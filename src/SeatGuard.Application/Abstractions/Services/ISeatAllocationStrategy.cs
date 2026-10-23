using SeatGuard.Domain.Reservations;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Application.Abstractions.Services;

/// <summary>
/// Claims a seat for a reservation. Two implementations exist: pessimistic
/// (row lock) and optimistic (xmin version check). Both must produce identical
/// semantics: exactly one reservation can win a seat.
/// </summary>
public interface ISeatAllocationStrategy
{
    Task<Seat?> AcquireAsync(
        SeatId seatId,
        ReservationId reservationId,
        CancellationToken cancellationToken = default);
}
