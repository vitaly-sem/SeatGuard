using Microsoft.EntityFrameworkCore;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Domain.Events;

namespace SeatGuard.Infrastructure.Persistence.Repositories;

internal sealed class EventRepository(SeatGuardDbContext dbContext) : IEventRepository
{
    public async Task AddAsync(Event aggregate, CancellationToken cancellationToken = default)
        => await dbContext.Events.AddAsync(aggregate, cancellationToken);

    public Task<Event?> FindAsync(EventId id, CancellationToken cancellationToken = default)
        => dbContext.Events.SingleOrDefaultAsync(@event => @event.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Event> Items, int TotalCount)> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Events.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(@event => @event.StartsAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
