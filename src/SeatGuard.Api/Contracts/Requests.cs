namespace SeatGuard.Api.Contracts;

public sealed record SeatSectionRequest(string Name, int Rows, int SeatsPerRow, decimal PriceAmount, string Currency);

public sealed record CreateEventRequest(
    string Name,
    string Venue,
    DateTimeOffset StartsAt,
    IReadOnlyList<SeatSectionRequest> Sections);

public sealed record ReserveSeatRequest(Guid EventId, Guid SeatId, string CustomerId);
