namespace SeatGuard.Application.Contracts;

public sealed record SeatAvailabilityDto(
    Guid Id,
    string Label,
    string Section,
    string Row,
    int Number,
    string Status,
    decimal PriceAmount,
    string Currency);

public sealed record EventAvailabilityDto(
    Guid EventId,
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<SeatAvailabilityDto> Seats);
