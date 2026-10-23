using FluentAssertions;
using SeatGuard.Domain.Common;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Reservations;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Domain.Tests.Reservations;

public sealed class ReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(10);

    [Fact]
    public void Create_starts_a_pending_reservation_with_a_ten_minute_hold()
    {
        var reservation = CreateReservation();

        reservation.Status.Should().Be(ReservationStatus.Pending);
        reservation.ExpiresAt.Should().Be(Now.Add(HoldDuration));
        reservation.TicketCode.Should().BeNull();
        reservation.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ReservationCreatedDomainEvent>();
    }

    [Fact]
    public void Create_uses_the_supplied_reservation_id()
    {
        var id = ReservationId.New();

        var reservation = Reservation.Create(
            EventId.New(),
            SeatId.New(),
            CustomerId.Create("customer-1"),
            Money.Eur(89),
            HoldDuration,
            Now,
            idempotencyKey: null,
            reservationId: id);

        reservation.Id.Should().Be(id);
    }

    [Fact]
    public void Create_normalizes_the_idempotency_key()
    {
        var reservation = CreateReservation(idempotencyKey: "  key-123  ");

        reservation.IdempotencyKey.Should().Be("key-123");
    }

    [Fact]
    public void Create_rejects_an_overlong_idempotency_key()
    {
        var action = () => CreateReservation(idempotencyKey: new string('x', 129));

        action.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Confirm_issues_a_ticket_and_is_idempotent()
    {
        var reservation = CreateReservation();

        var ticket = reservation.Confirm(Now.AddMinutes(1), "pay_1");
        var repeated = reservation.Confirm(Now.AddMinutes(2), "pay_2");

        repeated.Should().Be(ticket);
        ticket.Should().StartWith("SG-").And.HaveLength(23);
        reservation.Status.Should().Be(ReservationStatus.Confirmed);
        reservation.PaymentReference.Should().Be("pay_1");
        reservation.ConfirmedAt.Should().Be(Now.AddMinutes(1));
        reservation.DomainEvents.OfType<ReservationConfirmedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void Confirm_rejects_an_expired_hold()
    {
        var reservation = CreateReservation();

        var action = () => reservation.Confirm(Now.AddMinutes(11), "pay_1");

        action.Should().Throw<ReservationExpiredException>();
        reservation.Status.Should().Be(ReservationStatus.Pending);
    }

    [Fact]
    public void Confirm_rejects_a_cancelled_reservation()
    {
        var reservation = CreateReservation();
        reservation.Cancel(Now.AddMinutes(1));

        var action = () => reservation.Confirm(Now.AddMinutes(2), "pay_1");

        action.Should().Throw<InvalidReservationStateException>();
    }

    [Fact]
    public void Cancel_is_idempotent_and_rejected_after_confirmation()
    {
        var pending = CreateReservation();
        pending.Cancel(Now.AddMinutes(1)).Should().BeTrue();
        pending.Cancel(Now.AddMinutes(2)).Should().BeFalse();
        pending.DomainEvents.OfType<ReservationCancelledDomainEvent>().Should().HaveCount(1);

        var confirmed = CreateReservation();
        confirmed.Confirm(Now.AddMinutes(1), "pay_1");

        var action = () => confirmed.Cancel(Now.AddMinutes(2));
        action.Should().Throw<InvalidReservationStateException>();
    }

    [Fact]
    public void Expire_only_runs_after_the_hold_elapsed_and_only_once()
    {
        var reservation = CreateReservation();

        reservation.Expire(Now.AddMinutes(9)).Should().BeFalse();
        reservation.Expire(Now.AddMinutes(10)).Should().BeTrue();
        reservation.Expire(Now.AddMinutes(11)).Should().BeFalse();
        reservation.Status.Should().Be(ReservationStatus.Expired);
        reservation.DomainEvents.OfType<ReservationExpiredDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void Expire_leaves_a_confirmed_reservation_untouched()
    {
        var reservation = CreateReservation();
        reservation.Confirm(Now.AddMinutes(1), "pay_1");

        reservation.Expire(Now.AddMinutes(30)).Should().BeFalse();
        reservation.Status.Should().Be(ReservationStatus.Confirmed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(24 * 60 + 1)]
    public void Create_rejects_invalid_hold_duration(int minutes)
    {
        var action = () => Reservation.Create(
            EventId.New(),
            SeatId.New(),
            CustomerId.Create("customer-1"),
            Money.Eur(10),
            TimeSpan.FromMinutes(minutes),
            Now);

        action.Should().Throw<DomainValidationException>();
    }

    private static Reservation CreateReservation(string? idempotencyKey = null)
        => Reservation.Create(
            EventId.New(),
            SeatId.New(),
            CustomerId.Create("customer-1"),
            Money.Eur(89),
            HoldDuration,
            Now,
            idempotencyKey);
}
