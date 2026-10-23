using SeatGuard.Domain.Common;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Reservations;

namespace SeatGuard.Domain.Seats;

/// <summary>
/// A single seat. It is its own aggregate root because a booking updates exactly
/// one seat and must not lock or load the entire event.
/// </summary>
public sealed class Seat : AggregateRoot
{
    public SeatId Id { get; private set; }

    public EventId EventId { get; private set; }

    public string Section { get; private set; } = null!;

    public string Row { get; private set; } = null!;

    public int Number { get; private set; }

    public string Label { get; private set; } = null!;

    public Money Price { get; private set; } = null!;

    public SeatStatus Status { get; private set; }

    public ReservationId? HeldByReservationId { get; private set; }

    public DateTimeOffset? SoldAt { get; private set; }

    /// <summary>
    /// PostgreSQL xmin exposed as a row version. Used by the optimistic seat
    /// allocation strategy and maintained by the database.
    /// </summary>
    public uint Version { get; private set; }

    private Seat()
    {
    }

    public static Seat Create(EventId eventId, string section, string row, int number, Money price)
    {
        ArgumentNullException.ThrowIfNull(price);

        var normalizedSection = section?.Trim();
        var normalizedRow = row?.Trim();

        if (string.IsNullOrEmpty(normalizedSection))
        {
            throw new DomainValidationException("Section is required.");
        }

        if (string.IsNullOrEmpty(normalizedRow))
        {
            throw new DomainValidationException("Row is required.");
        }

        if (normalizedSection.Length > 100 || normalizedRow.Length > 10)
        {
            throw new DomainValidationException("Section or row label is too long.");
        }

        if (number < 1)
        {
            throw new DomainValidationException("Seat number must be positive.");
        }

        if (eventId.IsEmpty)
        {
            throw new DomainValidationException("Event id is required.");
        }

        return new Seat
        {
            Id = SeatId.New(),
            EventId = eventId,
            Section = normalizedSection,
            Row = normalizedRow,
            Number = number,
            Label = SeatLabel.Compose(normalizedSection, normalizedRow, number),

            // Each seat owns its own Money instance. Sharing one owned instance
            // across entities makes EF Core track it under several owners and
            // only persist it for one of them.
            Price = Money.Create(price.Amount, price.Currency),
            Status = SeatStatus.Available
        };
    }

    /// <summary>
    /// Marks the seat as held by a pending reservation. Repeating the call with
    /// the same reservation is a no-op so that retries stay safe.
    /// </summary>
    public void Hold(ReservationId reservationId)
    {
        if (Status == SeatStatus.Held && HeldByReservationId == reservationId)
        {
            return;
        }

        if (Status != SeatStatus.Available)
        {
            throw new SeatUnavailableException(Id, Status);
        }

        Status = SeatStatus.Held;
        HeldByReservationId = reservationId;
    }

    /// <summary>
    /// Converts a hold into a sale. Only the reservation that owns the hold may
    /// do this, and the call is idempotent for that reservation.
    /// </summary>
    public void Sell(ReservationId reservationId, DateTimeOffset now)
    {
        if (Status == SeatStatus.Sold && HeldByReservationId == reservationId)
        {
            return;
        }

        if (Status != SeatStatus.Held || HeldByReservationId != reservationId)
        {
            throw new InvalidSeatStateException(Id, "cannot be sold because it is not held by this reservation.");
        }

        Status = SeatStatus.Sold;
        SoldAt = now;
        Raise(new SeatSoldDomainEvent(Id, reservationId, now));
    }

    /// <summary>
    /// Returns a held seat to the pool. Releasing a seat that is already available
    /// is a no-op; releasing a seat held by somebody else is a programming error.
    /// </summary>
    public void Release(ReservationId reservationId)
    {
        if (Status == SeatStatus.Available)
        {
            return;
        }

        if (Status == SeatStatus.Sold)
        {
            throw new InvalidSeatStateException(Id, "has been sold and cannot be released.");
        }

        if (HeldByReservationId != reservationId)
        {
            throw new InvalidSeatStateException(Id, "is held by a different reservation.");
        }

        Status = SeatStatus.Available;
        HeldByReservationId = null;
        SoldAt = null;
    }
}
