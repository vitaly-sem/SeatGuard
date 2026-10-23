namespace SeatGuard.Application.Contracts;

public sealed record EventSectionDto(
    string Name,
    int Rows,
    int SeatsPerRow,
    decimal PriceAmount,
    string Currency);

public sealed record EventSummaryDto(
    Guid Id,
    string Name,
    string Venue,
    DateTimeOffset StartsAt,
    string Status,
    int SeatCount);

public sealed record EventDetailsDto(
    Guid Id,
    string Name,
    string Venue,
    DateTimeOffset StartsAt,
    string Status,
    int SeatCount,
    IReadOnlyList<EventSectionDto> Sections);

public sealed record CreateEventResult(Guid EventId, int SeatCount);
