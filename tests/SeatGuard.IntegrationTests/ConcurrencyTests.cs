using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeatGuard.Domain.Seats;
using SeatGuard.Infrastructure.Persistence;
using SeatGuard.IntegrationTests.Infrastructure;

namespace SeatGuard.IntegrationTests;

[Collection(IntegrationSuite.Name)]
public sealed class ConcurrencyTests
{
    private readonly SeatGuardFixture _fixture;

    public ConcurrencyTests(SeatGuardFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("Pessimistic")]
    [InlineData("Optimistic")]
    public async Task ConcurrentHolds_OnSameSeat_ProduceExactlyOneWinner(string strategy)
    {
        await using var app = await _fixture.StartAppAsync(strategy);
        var client = app.Client;

        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);
        var seatId = (await client.GetAvailabilityAsync(eventId)).SeatId("Orchestra · A1");
        const int attempts = 40;

        var calls = Enumerable.Range(0, attempts)
            .Select(index => client.ReserveAsync(eventId, seatId, $"racer-{index}"))
            .ToArray();

        var results = await Task.WhenAll(calls);

        try
        {
            results.Count(result => result.Response.StatusCode == HttpStatusCode.Created)
                .Should().Be(1);

            var losers = results
                .Where(result => result.Response.StatusCode == HttpStatusCode.Conflict)
                .ToArray();

            losers.Should().HaveCount(attempts - 1);

            foreach (var loser in losers)
            {
                loser.Body.ProblemCode().Should().BeOneOf("seat.not_available", "seat.conflict");
            }

            var winner = results.Single(result => result.Response.StatusCode == HttpStatusCode.Created);
            winner.Body.GetProperty("status").GetString().Should().Be("Pending");

            (await client.GetAvailabilityAsync(eventId)).SeatStatus("Orchestra · A1").Should().Be("Held");

            await using var scope = app.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();
            var stored = await db.Reservations
                .CountAsync(reservation => reservation.SeatId == new SeatId(seatId));

            stored.Should().Be(1);
        }
        finally
        {
            foreach (var (response, _) in results)
            {
                response.Dispose();
            }
        }
    }
}
