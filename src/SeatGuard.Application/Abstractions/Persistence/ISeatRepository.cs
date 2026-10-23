using SeatGuard.Domain.Events;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Application.Abstractions.Persistence;

public interface ISeatRepository
{
    Task AddRangeAsync(IEnumerable<Seat> seats, CancellationToken cancellationToken = default);

    Task<Seat?> FindAsync(SeatId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a seat while taking a row-level lock (SELECT ... FOR UPDATE).
    /// Must be called inside an active transaction.
    /// </summary>
    Task<Seat?> FindForUpdateAsync(SeatId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Seat>> ListByEventAsync(EventId eventId, CancellationToken cancellationToken = default);
}
