using SeatGuard.Domain.Reservations;

namespace SeatGuard.Application.Abstractions.Persistence;

public interface IReservationRepository
{
    Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default);

    Task<Reservation?> FindAsync(ReservationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a reservation while taking a row-level lock. Used by the confirm and
    /// cancel paths so that a concurrent sweeper cannot expire the hold midway.
    /// </summary>
    Task<Reservation?> FindForUpdateAsync(ReservationId id, CancellationToken cancellationToken = default);

    Task<Reservation?> FindByIdempotencyKeyAsync(
        CustomerId customerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
