using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SeatGuard.Application.Abstractions.Payments;
using SeatGuard.Application.Abstractions.Services;
using SeatGuard.Infrastructure.Persistence;
using SeatGuard.IntegrationTests.Infrastructure;

namespace SeatGuard.IntegrationTests;

/// <summary>
/// Regression coverage for two confirmation races:
///  - two clients confirming the same hold at the same time must get one ticket;
///  - a payment that outlives the hold window must not confirm the reservation.
/// </summary>
[Collection(IntegrationSuite.Name)]
public sealed class ConfirmRaceTests
{
    private readonly SeatGuardFixture _fixture;

    public ConfirmRaceTests(SeatGuardFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("Pessimistic")]
    [InlineData("Optimistic")]
    public async Task ConcurrentConfirms_IssueOneTicketAndOneConfirmationEvent(string strategy)
    {
        var gateway = new BarrierPaymentGateway(expectedCharges: 2);

        await using var app = await _fixture.StartAppAsync(strategy, services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(gateway);
        });

        var client = app.Client;
        var (eventId, seatId, reservationId) = await CreatePendingHoldAsync(client);

        var first = client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", null);
        var second = client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", null);

        var responses = await Task.WhenAll(first, second);

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);

        var bodies = await Task.WhenAll(responses.Select(ReadJsonAsync));
        var tickets = bodies.Select(body => body.GetProperty("ticketCode").GetString()).ToArray();

        tickets[0].Should().NotBeNullOrWhiteSpace();
        tickets[1].Should().Be(tickets[0], "a hold can only be confirmed once");

        await using var scope = app.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();

        var stored = await db.Reservations.SingleAsync(reservation => reservation.Id == new SeatGuard.Domain.Reservations.ReservationId(reservationId));
        stored.TicketCode.Should().Be(tickets[0]);

        var outboxTypes = await db.OutboxMessages.Select(message => message.Type).ToListAsync();
        outboxTypes.Count(type => type.EndsWith("ReservationConfirmedDomainEvent", StringComparison.Ordinal))
            .Should().Be(1);

        var soldSeats = await db.Seats.CountAsync(seat => seat.Id == new SeatGuard.Domain.Seats.SeatId(seatId));
        soldSeats.Should().Be(1);
    }

    [Fact]
    public async Task ConfirmWhosePaymentOutlivesTheHold_ReturnsGoneAndRefunds()
    {
        var gateway = new GatedPaymentGateway();

        await using var app = await _fixture.StartAppAsync(configureServices: services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(gateway);
        });

        var client = app.Client;
        var (_, seatId, reservationId) = await CreatePendingHoldAsync(client);

        var pendingConfirm = client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", null);

        await gateway.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // The hold expires while the payment provider is still processing.
        _fixture.Clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
        gateway.Release();

        var confirm = await pendingConfirm;
        confirm.StatusCode.Should().Be(HttpStatusCode.Gone);

        var problem = await ReadJsonAsync(confirm);
        problem.GetProperty("code").GetString().Should().Be("reservation.expired");

        gateway.Refunds.Should().Be(1);

        await using var scope = app.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();

        var stored = await db.Reservations.SingleAsync(reservation => reservation.Id == new SeatGuard.Domain.Reservations.ReservationId(reservationId));
        stored.Status.Should().Be(SeatGuard.Domain.Reservations.ReservationStatus.Pending);
        stored.TicketCode.Should().BeNull();
        stored.PaymentReference.Should().BeNull();

        var seat = await db.Seats.SingleAsync(candidate => candidate.Id == new SeatGuard.Domain.Seats.SeatId(seatId));
        seat.Status.Should().Be(SeatGuard.Domain.Seats.SeatStatus.Held);
    }

    [Fact]
    public async Task ConfirmAfterSweeperExpiry_ReturnsGoneNotConflict()
    {
        var gateway = new GatedPaymentGateway();

        await using var app = await _fixture.StartAppAsync(configureServices: services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(gateway);
        });

        var client = app.Client;
        var (_, seatId, reservationId) = await CreatePendingHoldAsync(client);

        var pendingConfirm = client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", null);

        await gateway.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        _fixture.Clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));

        await using (var scope = app.CreateScope())
        {
            var sweeper = scope.ServiceProvider.GetRequiredService<IExpiredReservationSweeper>();
            await sweeper.SweepOnceAsync();
        }

        gateway.Release();

        var confirm = await pendingConfirm;
        confirm.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await ReadJsonAsync(confirm)).GetProperty("code").GetString().Should().Be("reservation.expired");

        gateway.Refunds.Should().Be(1);

        await using var finalScope = app.CreateScope();
        var db = finalScope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();

        var seat = await db.Seats.SingleAsync(candidate => candidate.Id == new SeatGuard.Domain.Seats.SeatId(seatId));
        seat.Status.Should().Be(SeatGuard.Domain.Seats.SeatStatus.Available);
    }

    private async Task<(Guid EventId, Guid SeatId, Guid ReservationId)> CreatePendingHoldAsync(HttpClient client)
    {
        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);
        var seatId = (await client.GetAvailabilityAsync(eventId)).SeatId("Orchestra · A1");

        var (reserve, reservation) = await client.ReserveAsync(eventId, seatId, "customer-1");
        reserve.StatusCode.Should().Be(HttpStatusCode.Created);

        return (eventId, seatId, reservation.GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>
    /// Blocks every charge until the expected number of requests has passed the
    /// preflight checks, then releases all of them at once.
    /// </summary>
    private sealed class BarrierPaymentGateway(int expectedCharges) : IPaymentGateway
    {
        private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _charges;

        public async Task<PaymentResult> ChargeAsync(
            PaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _charges) == expectedCharges)
            {
                _arrived.TrySetResult();
            }

            await _arrived.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);

            return PaymentResult.Approved($"pay_{request.ReservationId.Value:N}");
        }

        public Task RefundAsync(string paymentReference, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Holds the charge until the test releases it and records compensations.
    /// </summary>
    private sealed class GatedPaymentGateway : IPaymentGateway
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Refunds { get; private set; }

        public void Release() => _release.TrySetResult();

        public async Task<PaymentResult> ChargeAsync(
            PaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);

            return PaymentResult.Approved($"pay_{request.ReservationId.Value:N}");
        }

        public Task RefundAsync(string paymentReference, CancellationToken cancellationToken = default)
        {
            Refunds++;
            return Task.CompletedTask;
        }
    }
}
