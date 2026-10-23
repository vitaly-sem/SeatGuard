using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeatGuard.Domain.Seats;
using SeatGuard.Infrastructure.Persistence;
using SeatGuard.IntegrationTests.Infrastructure;

namespace SeatGuard.IntegrationTests;

[Collection(IntegrationSuite.Name)]
public sealed class BookingApiTests
{
    private readonly SeatGuardFixture _fixture;

    public BookingApiTests(SeatGuardFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Root_RedirectsToConsole_AndServesAssets()
    {
        await using var app = await _fixture.StartAppAsync();
        using var client = app.CreateClient(allowAutoRedirect: false);

        var root = await client.GetAsync("/");
        root.StatusCode.Should().Be(HttpStatusCode.Found);
        root.Headers.Location!.OriginalString.Should().Be("/ui/");

        var page = await client.GetAsync("/ui/");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync()).Should().Contain("Booking console");

        (await client.GetAsync("/ui/app.js")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/ui/app.css")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateEvent_WithoutAdminKey_ReturnsUnauthorized()
    {
        await using var app = await _fixture.StartAppAsync();

        var response = await app.Client.PostAsJsonAsync("/api/v1/events", new
        {
            name = "No key",
            venue = "Hall",
            startsAt = _fixture.Clock.GetUtcNow().AddDays(10),
            sections = Array.Empty<object>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateEvent_PersistsSectionsSeatsAndCacheHeaders()
    {
        await using var app = await _fixture.StartAppAsync();
        var client = app.Client;

        var eventId = await client.CreateEventAsync(
            _fixture.Clock.GetUtcNow().AddDays(30),
            rows: 2,
            seatsPerRow: 3);

        var details = await client.GetFromJsonAsync<JsonElement>($"/api/v1/events/{eventId}");
        details.GetProperty("seatCount").GetInt32().Should().Be(6);
        details.GetProperty("sections").GetArrayLength().Should().Be(1);
        details.GetProperty("sections")[0].GetProperty("rows").GetInt32().Should().Be(2);
        details.GetProperty("sections")[0].GetProperty("seatsPerRow").GetInt32().Should().Be(3);

        var response = await client.GetAsync($"/api/v1/events/{eventId}/availability");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromSeconds(2));

        var availability = await response.Content.ReadFromJsonAsync<JsonElement>();
        availability.GetProperty("seats").GetArrayLength().Should().Be(6);
        availability.GetProperty("seats").EnumerateArray()
            .Should().OnlyContain(seat => seat.Status() == "Available");

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/events?page=1&pageSize=10");
        list.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ReserveConfirm_IssuesTicketAndMarksSeatSold()
    {
        await using var app = await _fixture.StartAppAsync();
        var client = app.Client;

        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 2);
        var availability = await client.GetAvailabilityAsync(eventId);
        var seatId = availability.SeatId("Orchestra · A1");

        var (reserve, reservation) = await client.ReserveAsync(eventId, seatId, "customer-1");

        reserve.StatusCode.Should().Be(HttpStatusCode.Created);
        reservation.GetProperty("status").GetString().Should().Be("Pending");
        reservation.GetProperty("amount").GetDecimal().Should().Be(49m);

        var createdAt = reservation.GetProperty("createdAt").GetDateTimeOffset();
        var expiresAt = reservation.GetProperty("expiresAt").GetDateTimeOffset();
        (expiresAt - createdAt).Should().Be(TimeSpan.FromMinutes(5));

        var reservationId = reservation.GetProperty("id").GetGuid();

        var confirm = await client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", null);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmed = await confirm.Content.ReadFromJsonAsync<JsonElement>();
        confirmed.GetProperty("status").GetString().Should().Be("Confirmed");
        var ticket = confirmed.GetProperty("ticketCode").GetString();
        ticket.Should().StartWith("SG-");

        var replay = await client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", null);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("ticketCode").GetString().Should().Be(ticket);

        var (secondHold, conflict) = await client.ReserveAsync(eventId, seatId, "customer-2");
        secondHold.StatusCode.Should().Be(HttpStatusCode.Conflict);
        conflict.ProblemCode().Should().Be("seat.not_available");

        var cancel = await client.PostAsync($"/api/v1/reservations/{reservationId}/cancel", null);
        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await cancel.Content.ReadFromJsonAsync<JsonElement>())
            .ProblemCode().Should().Be("reservation.invalid_state");

        var after = await client.GetAvailabilityAsync(eventId);
        after.SeatStatus("Orchestra · A1").Should().Be("Sold");
    }

    [Fact]
    public async Task CancelPendingHold_ReleasesSeatForRebooking()
    {
        await using var app = await _fixture.StartAppAsync();
        var client = app.Client;

        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);
        var seatId = (await client.GetAvailabilityAsync(eventId)).SeatId("Orchestra · A1");

        var (reserve, reservation) = await client.ReserveAsync(eventId, seatId, "customer-1");
        reserve.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = reservation.GetProperty("id").GetGuid();

        var cancel = await client.PostAsync($"/api/v1/reservations/{reservationId}/cancel", null);
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetAvailabilityAsync(eventId)).SeatStatus("Orchestra · A1").Should().Be("Available");

        var (rebook, _) = await client.ReserveAsync(eventId, seatId, "customer-2");
        rebook.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task IdempotentReserve_ReplaysTheSameReservation()
    {
        await using var app = await _fixture.StartAppAsync();
        var client = app.Client;

        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 2);
        var seatId = (await client.GetAvailabilityAsync(eventId)).SeatId("Orchestra · A1");
        var key = $"idem-{Guid.NewGuid():N}";

        var (first, firstBody) = await client.ReserveAsync(eventId, seatId, "customer-1", key);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = firstBody.GetProperty("id").GetGuid();

        var (second, secondBody) = await client.ReserveAsync(eventId, seatId, "customer-1", key);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        secondBody.GetProperty("id").GetGuid().Should().Be(reservationId);

        await using var scope = app.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();
        var stored = await db.Reservations.CountAsync(reservation => reservation.SeatId == new SeatId(seatId));
        stored.Should().Be(1);
    }

    [Fact]
    public async Task Reserve_WithInvalidCustomer_ReturnsCamelCaseValidationErrors()
    {
        await using var app = await _fixture.StartAppAsync();
        var client = app.Client;

        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);
        var seatId = (await client.GetAvailabilityAsync(eventId)).SeatId("Orchestra · A1");

        var (response, problem) = await client.ReserveAsync(eventId, seatId, customerId: string.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.ProblemCode().Should().Be("request.invalid");
        problem.HasError("customerId").Should().BeTrue();
    }

    [Fact]
    public async Task Reserve_UnknownSeat_ReturnsNotFound()
    {
        await using var app = await _fixture.StartAppAsync();
        var client = app.Client;

        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);

        var (response, problem) = await client.ReserveAsync(eventId, Guid.NewGuid(), "customer-1");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        problem.ProblemCode().Should().Be("resource.not_found");
    }

    [Fact]
    public async Task Availability_IsInvalidatedWhenSeatIsHeld()
    {
        await using var app = await _fixture.StartAppAsync();
        var client = app.Client;

        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);
        var seatId = (await client.GetAvailabilityAsync(eventId)).SeatId("Orchestra · A1");

        var (reserve, _) = await client.ReserveAsync(eventId, seatId, "customer-1");
        reserve.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await client.GetAvailabilityAsync(eventId);
        after.SeatStatus("Orchestra · A1").Should().Be("Held");
    }
}
