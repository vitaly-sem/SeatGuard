using Microsoft.EntityFrameworkCore;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Domain.Reservations;

namespace SeatGuard.Infrastructure.Persistence.Repositories;

internal sealed class ReservationRepository(SeatGuardDbContext dbContext) : IReservationRepository
{
    public async Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default)
        => await dbContext.Reservations.AddAsync(reservation, cancellationToken);

    public Task<Reservation?> FindAsync(ReservationId id, CancellationToken cancellationToken = default)
        => dbContext.Reservations.SingleOrDefaultAsync(reservation => reservation.Id == id, cancellationToken);

    public async Task<Reservation?> FindForUpdateAsync(
        ReservationId id,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("FindForUpdateAsync requires an active transaction.");
        }

        // SKIP LOCKED is deliberately not used here: confirm/cancel must wait for
        // the sweeper to finish and then observe the row's final state.
        var reservations = await dbContext.Reservations
            .FromSqlInterpolated($"SELECT * FROM reservations WHERE id = {id.Value} FOR UPDATE")
            .ToListAsync(cancellationToken);

        return reservations.SingleOrDefault();
    }

    public Task<Reservation?> FindByIdempotencyKeyAsync(
        CustomerId customerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
        => dbContext.Reservations.SingleOrDefaultAsync(
            reservation => reservation.CustomerId == customerId
                && reservation.IdempotencyKey == idempotencyKey,
            cancellationToken);
}
