using FluentAssertions;
using SeatGuard.Domain.Common;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Reservations;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Domain.Tests.Seats;

public sealed class SeatTests
{
    private static readonly EventId EventId = EventId.New();
    private static readonly ReservationId FirstReservation = ReservationId.New();
    private static readonly ReservationId SecondReservation = ReservationId.New();

    [Fact]
    public void Create_builds_available_seat_with_composed_label()
    {
        var seat = CreateSeat();

        seat.Status.Should().Be(SeatStatus.Available);
        seat.Label.Should().Be("Orchestra · C7");
        seat.HeldByReservationId.Should().BeNull();
    }

    [Fact]
    public void Hold_moves_seat_to_held_and_is_idempotent_for_same_reservation()
    {
        var seat = CreateSeat();

        seat.Hold(FirstReservation);
        seat.Hold(FirstReservation);

        seat.Status.Should().Be(SeatStatus.Held);
        seat.HeldByReservationId.Should().Be(FirstReservation);
    }

    [Fact]
    public void Hold_rejects_a_seat_held_by_another_reservation()
    {
        var seat = CreateSeat();
        seat.Hold(FirstReservation);

        var action = () => seat.Hold(SecondReservation);

        action.Should().Throw<SeatUnavailableException>()
            .Which.Status.Should().Be(SeatStatus.Held);
    }

    [Fact]
    public void Sell_requires_the_owning_reservation()
    {
        var seat = CreateSeat();
        seat.Hold(FirstReservation);

        var wrongOwner = () => seat.Sell(SecondReservation, DateTimeOffset.UtcNow);
        wrongOwner.Should().Throw<InvalidSeatStateException>();

        seat.Sell(FirstReservation, DateTimeOffset.UtcNow);
        seat.Status.Should().Be(SeatStatus.Sold);
        seat.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SeatSoldDomainEvent>();
    }

    [Fact]
    public void Sell_is_idempotent_for_the_owning_reservation()
    {
        var seat = CreateSeat();
        seat.Hold(FirstReservation);
        seat.Sell(FirstReservation, DateTimeOffset.UtcNow);

        seat.Sell(FirstReservation, DateTimeOffset.UtcNow);

        seat.Status.Should().Be(SeatStatus.Sold);
        seat.DomainEvents.Should().HaveCount(1);
    }

    [Fact]
    public void Release_returns_a_held_seat_to_the_pool()
    {
        var seat = CreateSeat();
        seat.Hold(FirstReservation);

        seat.Release(FirstReservation);

        seat.Status.Should().Be(SeatStatus.Available);
        seat.HeldByReservationId.Should().BeNull();
    }

    [Fact]
    public void Release_rejects_a_seat_held_by_another_reservation()
    {
        var seat = CreateSeat();
        seat.Hold(FirstReservation);

        var action = () => seat.Release(SecondReservation);

        action.Should().Throw<InvalidSeatStateException>().WithMessage("*different reservation*");
    }

    [Fact]
    public void Release_rejects_a_sold_seat()
    {
        var seat = CreateSeat();
        seat.Hold(FirstReservation);
        seat.Sell(FirstReservation, DateTimeOffset.UtcNow);

        var action = () => seat.Release(FirstReservation);

        action.Should().Throw<InvalidSeatStateException>().WithMessage("*sold*");
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    public void RowLabel_uses_spreadsheet_notation(int index, string expected)
        => SeatLabel.RowLabel(index).Should().Be(expected);

    [Fact]
    public void Create_rejects_non_positive_seat_number()
    {
        var action = () => Seat.Create(EventId, "Orchestra", "A", 0, Money.Eur(10));

        action.Should().Throw<DomainValidationException>();
    }

    private static Seat CreateSeat()
        => Seat.Create(EventId, "Orchestra", "C", 7, Money.Eur(89));
}
