namespace SeatGuard.Domain.Common;

/// <summary>
/// Base class for aggregate roots. Aggregates are the only entities that are
/// loaded and saved as a unit and the only ones allowed to raise domain events.
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
