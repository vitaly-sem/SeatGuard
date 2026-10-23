using SeatGuard.Domain.Events;

namespace SeatGuard.Application.Abstractions.Persistence;

public interface IEventRepository
{
    Task AddAsync(Event aggregate, CancellationToken cancellationToken = default);

    Task<Event?> FindAsync(EventId id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Event> Items, int TotalCount)> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
