using Microsoft.EntityFrameworkCore;
using SeatGuard.Domain.Common;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Reservations;
using SeatGuard.Domain.Seats;
using SeatGuard.Infrastructure.Outbox;

namespace SeatGuard.Infrastructure.Persistence;

public sealed class SeatGuardDbContext(
    DbContextOptions<SeatGuardDbContext> options,
    TimeProvider timeProvider) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();

    public DbSet<Seat> Seats => Set<Seat>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SeatGuardDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var aggregatesWithEvents = ChangeTracker
            .Entries<AggregateRoot>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

        if (aggregatesWithEvents.Count > 0)
        {
            var now = timeProvider.GetUtcNow();
            var messages = aggregatesWithEvents
                .SelectMany(aggregate => aggregate.DomainEvents)
                .Select(domainEvent => OutboxMessage.FromDomainEvent(domainEvent, now));

            OutboxMessages.AddRange(messages);
        }

        var result = await base.SaveChangesAsync(cancellationToken);

        foreach (var aggregate in aggregatesWithEvents)
        {
            aggregate.ClearDomainEvents();
        }

        return result;
    }
}
