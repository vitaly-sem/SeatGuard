namespace SeatGuard.Application.Contracts;

public sealed record ReservationDto(
    Guid Id,
    Guid EventId,
    Guid SeatId,
    string CustomerId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConfirmedAt,
    string? TicketCode);

public sealed record ReserveSeatResult(ReservationDto Reservation, bool Replayed);
