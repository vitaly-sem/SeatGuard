using Microsoft.EntityFrameworkCore;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Infrastructure.Persistence.Repositories;

internal sealed class SeatRepository(SeatGuardDbContext dbContext) : ISeatRepository
{
    public async Task AddRangeAsync(IEnumerable<Seat> seats, CancellationToken cancellationToken = default)
        => await dbContext.Seats.AddRangeAsync(seats, cancellationToken);

    public Task<Seat?> FindAsync(SeatId id, CancellationToken cancellationToken = default)
        => dbContext.Seats.SingleOrDefaultAsync(seat => seat.Id == id, cancellationToken);

    /// <summary>
    /// Takes a row-level lock. The raw query must select xmin explicitly because
    /// SELECT * does not include PostgreSQL system columns and EF Core uses xmin
    /// as the optimistic concurrency token.
    /// </summary>
    public async Task<Seat?> FindForUpdateAsync(SeatId id, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("FindForUpdateAsync requires an active transaction.");
        }

        var seats = await dbContext.Seats
            .FromSqlInterpolated($"SELECT s.*, s.xmin FROM seats AS s WHERE s.id = {id.Value} FOR UPDATE")
            .ToListAsync(cancellationToken);

        return seats.SingleOrDefault();
    }

    public async Task<IReadOnlyList<Seat>> ListByEventAsync(
        EventId eventId,
        CancellationToken cancellationToken = default)
        => await dbContext.Seats
            .AsNoTracking()
            .Where(seat => seat.EventId == eventId)
            .OrderBy(seat => seat.Section)
            .ThenBy(seat => seat.Row)
            .ThenBy(seat => seat.Number)
            .ToListAsync(cancellationToken);
}
