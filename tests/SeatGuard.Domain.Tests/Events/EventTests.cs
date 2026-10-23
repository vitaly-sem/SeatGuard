using FluentAssertions;
using SeatGuard.Domain.Common;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Reservations;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Domain.Tests.Events;

public sealed class EventTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_builds_published_event_with_seat_count()
    {
        var @event = CreateEvent();

        @event.Status.Should().Be(EventStatus.Published);
        @event.SeatCount.Should().Be(30);
        @event.Sections.Should().HaveCount(1);
        @event.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<EventCreatedDomainEvent>();
    }

    [Fact]
    public void Create_rejects_event_in_the_past()
    {
        var action = () => Event.Create("Concert", "Hall", Now.AddMinutes(-1), [Section()], Now);

        action.Should().Throw<DomainValidationException>().WithMessage("*future*");
    }

    [Fact]
    public void Create_rejects_mixed_currencies()
    {
        var action = () => Event.Create(
            "Concert",
            "Hall",
            Now.AddDays(1),
            [Section(), new SeatSection("Balcony", 1, 1, Money.Create(10, "USD"))],
            Now);

        action.Should().Throw<DomainValidationException>().WithMessage("*same currency*");
    }

    [Fact]
    public void Create_rejects_duplicate_section_names()
    {
        var action = () => Event.Create(
            "Concert",
            "Hall",
            Now.AddDays(1),
            [Section(), new SeatSection("orchestra", 1, 1, Money.Eur(10))],
            Now);

        action.Should().Throw<DomainValidationException>().WithMessage("*unique*");
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(3, 0)]
    public void Create_rejects_invalid_dimensions(int rows, int seatsPerRow)
    {
        var action = () => Event.Create(
            "Concert",
            "Hall",
            Now.AddDays(1),
            [new SeatSection("Main", rows, seatsPerRow, Money.Eur(10))],
            Now);

        action.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Cancel_is_idempotent_and_rejected_after_start()
    {
        var @event = CreateEvent();

        @event.Cancel(Now);
        @event.Cancel(Now);
        @event.Status.Should().Be(EventStatus.Cancelled);

        var started = Event.Create("Concert", "Hall", Now.AddDays(1), [Section()], Now);
        var action = () => started.Cancel(Now.AddDays(2));

        action.Should().Throw<DomainValidationException>().WithMessage("*already started*");
    }

    private static Event CreateEvent()
        => Event.Create("Concert", "Hall", Now.AddDays(7), [Section()], Now);

    private static SeatSection Section() => new("Orchestra", 2, 15, Money.Eur(89));
}
