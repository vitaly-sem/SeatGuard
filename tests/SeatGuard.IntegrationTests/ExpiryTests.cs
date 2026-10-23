using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeatGuard.Application.Abstractions.Services;
using SeatGuard.Infrastructure.Persistence;
using SeatGuard.IntegrationTests.Infrastructure;

namespace SeatGuard.IntegrationTests;

[Collection(IntegrationSuite.Name)]
public sealed class ExpiryTests
{
    private readonly SeatGuardFixture _fixture;

    public ExpiryTests(SeatGuardFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ConfirmAfterExpiry_ReturnsGoneBeforeAnySweep()
    {
        await using var app = await _fixture.StartAppAsync();
        var client = app.Client;

        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);
        var seatId = (await client.GetAvailabilityAsync(eventId)).SeatId("Orchestra · A1");

        var (reserve, reservation) = await client.ReserveAsync(eventId, seatId, "customer-1");
        reserve.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = reservation.GetProperty("id").GetGuid();

        _fixture.Clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));

        var confirm = await client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", null);
        confirm.StatusCode.Should().Be(HttpStatusCode.Gone);

        var problem = await confirm.Content.ReadFromJsonAsync<JsonElement>();
        problem.ProblemCode().Should().Be("reservation.expired");
    }

    [Fact]
    public async Task Sweeper_ExpiresHold_ReleasesSeatAndWritesOutboxEvent()
    {
        await using var app = await _fixture.StartAppAsync();
        var client = app.Client;

        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);
        var seatId = (await client.GetAvailabilityAsync(eventId)).SeatId("Orchestra · A1");

        var (reserve, reservation) = await client.ReserveAsync(eventId, seatId, "customer-1");
        reserve.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = reservation.GetProperty("id").GetGuid();

        _fixture.Clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));

        await using (var scope = app.CreateScope())
        {
            var sweeper = scope.ServiceProvider.GetRequiredService<IExpiredReservationSweeper>();
            (await sweeper.SweepOnceAsync()).Should().Be(1);
            (await sweeper.SweepOnceAsync()).Should().Be(0);
        }

        var expired = await client.GetFromJsonAsync<JsonElement>($"/api/v1/reservations/{reservationId}");
        expired.GetProperty("status").GetString().Should().Be("Expired");

        (await client.GetAvailabilityAsync(eventId)).SeatStatus("Orchestra · A1").Should().Be("Available");

        var confirm = await client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", null);
        confirm.StatusCode.Should().Be(HttpStatusCode.Gone);

        var (rebook, _) = await client.ReserveAsync(eventId, seatId, "customer-2");
        rebook.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var finalScope = app.CreateScope();
        var db = finalScope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();
        var types = await db.OutboxMessages.Select(message => message.Type).ToListAsync();

        types.Should().Contain(type =>
            type.EndsWith("ReservationExpiredDomainEvent", StringComparison.Ordinal));
    }
}
