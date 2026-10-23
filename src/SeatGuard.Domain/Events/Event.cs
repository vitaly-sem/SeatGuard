using System.Diagnostics.CodeAnalysis;
using SeatGuard.Domain.Common;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Domain.Events;

/// <summary>
/// A scheduled event with a fixed seat map. Seats are separate aggregates so
/// that a booking never has to load the whole venue into memory.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "'Event' is the ubiquitous domain term for a ticketed happening and is not part of a public cross-language API.")]
public sealed class Event : AggregateRoot
{
    private const int MaxSeats = 10_000;

    public EventId Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Venue { get; private set; } = null!;

    public DateTimeOffset StartsAt { get; private set; }

    public EventStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int SeatCount { get; private set; }

    /// <summary>
    /// Mutable for EF Core materialization; callers must treat it as read-only.
    /// </summary>
    public List<SeatSection> Sections { get; private set; } = [];

    private Event()
    {
    }

    public static Event Create(
        string name,
        string venue,
        DateTimeOffset startsAt,
        IEnumerable<SeatSection> sections,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sections);

        var normalizedName = RequireText(name, "Event name", 200);
        var normalizedVenue = RequireText(venue, "Venue", 200);

        if (startsAt <= now)
        {
            throw new DomainValidationException("An event must start in the future.");
        }

        var sectionList = sections.ToList();
        ValidateSections(sectionList);

        var aggregate = new Event
        {
            Id = EventId.New(),
            Name = normalizedName,
            Venue = normalizedVenue,
            StartsAt = startsAt,
            Status = EventStatus.Published,
            CreatedAt = now,
            SeatCount = sectionList.Sum(section => section.Rows * section.SeatsPerRow),
            Sections = sectionList
        };

        aggregate.Raise(new EventCreatedDomainEvent(aggregate.Id, aggregate.Name, aggregate.SeatCount, now));

        return aggregate;
    }

    /// <summary>
    /// Guards the booking path. A cancelled event, or one that has already
    /// started, must not accept new reservations.
    /// </summary>
    public void EnsureBookable(DateTimeOffset now)
    {
        if (Status != EventStatus.Published)
        {
            throw new EventNotOnSaleException(Id, $"status is {Status}.");
        }

        if (now >= StartsAt)
        {
            throw new EventNotOnSaleException(Id, "the event has already started.");
        }
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == EventStatus.Cancelled)
        {
            return;
        }

        if (now >= StartsAt)
        {
            throw new DomainValidationException("An event that has already started cannot be cancelled.");
        }

        Status = EventStatus.Cancelled;
    }

    private static void ValidateSections(List<SeatSection> sections)
    {
        if (sections.Count == 0)
        {
            throw new DomainValidationException("An event must define at least one seat section.");
        }

        if (sections.Select(section => section.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sections.Count)
        {
            throw new DomainValidationException("Seat section names must be unique.");
        }

        var currencies = sections.Select(section => section.Price.Currency).Distinct(StringComparer.Ordinal).ToList();
        if (currencies.Count != 1)
        {
            throw new DomainValidationException("All seat sections of an event must use the same currency.");
        }

        foreach (var section in sections)
        {
            RequireText(section.Name, "Section name", 100);

            if (section.Price is null)
            {
                throw new DomainValidationException("Section price is required.");
            }

            if (section.Rows is < 1 or > 200)
            {
                throw new DomainValidationException("A section must have between 1 and 200 rows.");
            }

            if (section.SeatsPerRow is < 1 or > 200)
            {
                throw new DomainValidationException("A row must have between 1 and 200 seats.");
            }
        }

        if (sections.Sum(section => section.Rows * section.SeatsPerRow) > MaxSeats)
        {
            throw new DomainValidationException($"An event cannot contain more than {MaxSeats} seats.");
        }
    }

    private static string RequireText(string? value, string field, int maxLength)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrEmpty(normalized))
        {
            throw new DomainValidationException($"{field} is required.");
        }

        if (normalized.Length > maxLength)
        {
            throw new DomainValidationException($"{field} cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}
