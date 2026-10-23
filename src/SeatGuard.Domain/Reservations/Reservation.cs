using SeatGuard.Domain.Common;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Domain.Reservations;

/// <summary>
/// A time-boxed claim on a seat. The aggregate owns the reservation lifecycle;
/// the seat aggregate owns the physical availability of the seat. Both are
/// mutated inside one transaction, which keeps the two views consistent.
/// </summary>
public sealed class Reservation : AggregateRoot
{
    public const int MaxIdempotencyKeyLength = 128;

    public ReservationId Id { get; private set; }

    public EventId EventId { get; private set; }

    public SeatId SeatId { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public Money Amount { get; private set; } = null!;

    public ReservationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public DateTimeOffset? ExpiredAt { get; private set; }

    public string? TicketCode { get; private set; }

    public string? PaymentReference { get; private set; }

    public string? IdempotencyKey { get; private set; }

    private Reservation()
    {
    }

    public static Reservation Create(
        EventId eventId,
        SeatId seatId,
        CustomerId customerId,
        Money amount,
        TimeSpan holdDuration,
        DateTimeOffset now,
        string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (eventId.IsEmpty || seatId.IsEmpty)
        {
            throw new DomainValidationException("Event id and seat id are required.");
        }

        if (customerId.IsEmpty)
        {
            throw new DomainValidationException("Customer id is required.");
        }

        if (holdDuration <= TimeSpan.Zero || holdDuration > TimeSpan.FromHours(24))
        {
            throw new DomainValidationException("Hold duration must be between zero and 24 hours.");
        }

        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        var reservationId = ReservationId.New();
        var reservation = new Reservation
        {
            Id = reservationId,
            EventId = eventId,
            SeatId = seatId,
            CustomerId = customerId,
            Amount = amount,
            Status = ReservationStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.Add(holdDuration),
            IdempotencyKey = normalizedKey
        };

        reservation.Raise(new ReservationCreatedDomainEvent(
            reservationId,
            eventId,
            seatId,
            customerId,
            reservation.ExpiresAt,
            now));

        return reservation;
    }

    /// <summary>
    /// Confirms a pending reservation and issues a ticket. Calling Confirm on an
    /// already confirmed reservation returns the existing ticket so that client
    /// retries do not charge the customer twice.
    /// </summary>
    public string Confirm(DateTimeOffset now, string paymentReference)
    {
        if (Status == ReservationStatus.Confirmed)
        {
            return TicketCode!;
        }

        if (Status != ReservationStatus.Pending)
        {
            throw new InvalidReservationStateException(Id, Status, "only pending reservations can be confirmed.");
        }

        if (now >= ExpiresAt)
        {
            throw new ReservationExpiredException(Id, ExpiresAt);
        }

        if (string.IsNullOrWhiteSpace(paymentReference))
        {
            throw new DomainValidationException("Payment reference is required.");
        }

        Status = ReservationStatus.Confirmed;
        ConfirmedAt = now;
        PaymentReference = paymentReference;
        TicketCode = TicketCodeGenerator.Generate();

        Raise(new ReservationConfirmedDomainEvent(Id, SeatId, TicketCode, now));

        return TicketCode;
    }

    /// <summary>
    /// Cancels a pending reservation. Returns false when it was already cancelled,
    /// making the operation idempotent.
    /// </summary>
    public bool Cancel(DateTimeOffset now)
    {
        if (Status == ReservationStatus.Cancelled)
        {
            return false;
        }

        if (Status != ReservationStatus.Pending)
        {
            throw new InvalidReservationStateException(Id, Status, "only pending reservations can be cancelled.");
        }

        if (now >= ExpiresAt)
        {
            throw new ReservationExpiredException(Id, ExpiresAt);
        }

        Status = ReservationStatus.Cancelled;
        CancelledAt = now;
        Raise(new ReservationCancelledDomainEvent(Id, SeatId, now));

        return true;
    }

    /// <summary>
    /// Expires a pending reservation whose hold window has elapsed. Returns false
    /// when the reservation was confirmed (or already expired) in the meantime,
    /// which lets the sweeper skip it without treating it as an error.
    /// </summary>
    public bool Expire(DateTimeOffset now)
    {
        if (Status != ReservationStatus.Pending || now < ExpiresAt)
        {
            return false;
        }

        Status = ReservationStatus.Expired;
        ExpiredAt = now;
        Raise(new ReservationExpiredDomainEvent(Id, SeatId, now));

        return true;
    }

    public bool IsExpired(DateTimeOffset now) => Status == ReservationStatus.Pending && now >= ExpiresAt;

    public bool IsActive => Status is ReservationStatus.Pending or ReservationStatus.Confirmed;

    private static string? NormalizeIdempotencyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > MaxIdempotencyKeyLength)
        {
            throw new DomainValidationException(
                $"Idempotency key cannot exceed {MaxIdempotencyKeyLength} characters.");
        }

        return normalized;
    }
}
