namespace SeatGuard.Domain.Events;

/// <summary>
/// Strongly typed identifier for an event. Uses UUIDv7 so identifiers are
/// time-ordered, which keeps B-tree inserts sequential.
/// </summary>
public readonly record struct EventId(Guid Value)
{
    public static EventId New() => new(Guid.CreateVersion7());

    public static EventId Empty => default;

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}
