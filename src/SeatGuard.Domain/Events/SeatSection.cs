using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Events;

/// <summary>
/// Description of one price tier of a venue. The event aggregate owns the seat
/// map; individual seats are separate aggregates generated when the event is created.
/// </summary>
public sealed record SeatSection(string Name, int Rows, int SeatsPerRow, Money Price);
