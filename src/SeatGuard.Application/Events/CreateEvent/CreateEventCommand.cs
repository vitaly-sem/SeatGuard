using MediatR;
using SeatGuard.Application.Contracts;

namespace SeatGuard.Application.Events.CreateEvent;

public sealed record SeatSectionInput(string Name, int Rows, int SeatsPerRow, decimal PriceAmount, string Currency);

public sealed record CreateEventCommand(
    string Name,
    string Venue,
    DateTimeOffset StartsAt,
    IReadOnlyList<SeatSectionInput> Sections) : IRequest<CreateEventResult>;
