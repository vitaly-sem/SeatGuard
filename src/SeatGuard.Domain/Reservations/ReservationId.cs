namespace SeatGuard.Domain.Reservations;

/// <summary>
/// Strongly typed identifier for a reservation.
/// </summary>
public readonly record struct ReservationId(Guid Value)
{
    public static ReservationId New() => new(Guid.CreateVersion7());

    public static ReservationId Empty => default;

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();
}
