using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Events;

/// <summary>
/// Description of one price tier of a venue. The event aggregate owns the seat
/// map; individual seats are separate aggregates generated when the event is created.
/// </summary>
public sealed class SeatSection
{
    public string Name { get; private set; } = null!;

    public int Rows { get; private set; }

    public int SeatsPerRow { get; private set; }

    public Money Price { get; private set; } = null!;

    // Required by EF Core for JSON owned-type materialization.
    private SeatSection()
    {
    }

    public SeatSection(string name, int rows, int seatsPerRow, Money price)
    {
        Name = name;
        Rows = rows;
        SeatsPerRow = seatsPerRow;
        Price = price;
    }
}
