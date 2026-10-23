namespace SeatGuard.Domain.Common;

/// <summary>
/// Marker for a fact that happened inside the domain model. Domain events are
/// collected by aggregates and persisted to the transactional outbox by the
/// infrastructure layer when the aggregate is saved.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
