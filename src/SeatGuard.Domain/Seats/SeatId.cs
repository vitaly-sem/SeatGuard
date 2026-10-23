namespace SeatGuard.Domain.Seats;

/// <summary>
/// Strongly typed identifier for a seat.
/// </summary>
public readonly record struct SeatId(Guid Value)
{
    public static SeatId New() => new(Guid.CreateVersion7());

    public static SeatId Empty => default;

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}
