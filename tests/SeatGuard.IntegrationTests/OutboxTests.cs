using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SeatGuard.Application.Abstractions.Messaging;
using SeatGuard.Infrastructure.Persistence;
using SeatGuard.IntegrationTests.Infrastructure;

namespace SeatGuard.IntegrationTests;

[Collection(IntegrationSuite.Name)]
public sealed class OutboxTests
{
    private readonly SeatGuardFixture _fixture;

    public OutboxTests(SeatGuardFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Dispatcher_RetriesWithBackoff_ThenPublishesAllMessages()
    {
        var publisher = new SwitchablePublisher();

        await using var app = await _fixture.StartAppAsync(configureServices: services =>
        {
            services.RemoveAll<IIntegrationEventPublisher>();
            services.AddSingleton<IIntegrationEventPublisher>(publisher);
        });

        var client = app.Client;
        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);
        var seatId = (await client.GetAvailabilityAsync(eventId)).SeatId("Orchestra · A1");

        var (reserve, reservation) = await client.ReserveAsync(eventId, seatId, "customer-1");
        reserve.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = reservation.GetProperty("id").GetGuid();

        (await client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var now = _fixture.Clock.GetUtcNow();

        await using (var scope = app.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
            (await dispatcher.DispatchOnceAsync()).Should().Be(0);
        }

        await using (var scope = app.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();
            var rows = await db.OutboxMessages.AsNoTracking().ToListAsync();

            rows.Count.Should().BeGreaterThanOrEqualTo(4);
            rows.Should().OnlyContain(row => row.ProcessedAt == null && row.FailedAt == null);
            rows.Should().OnlyContain(row => row.Attempts == 1);
            rows.Should().OnlyContain(row => row.NextAttemptAt > now);
            rows.Should().OnlyContain(row => !string.IsNullOrEmpty(row.LastError));
        }

        publisher.Fail = false;
        _fixture.Clock.Advance(TimeSpan.FromSeconds(3));

        int published;

        await using (var scope = app.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
            published = await dispatcher.DispatchOnceAsync();
            published.Should().BeGreaterThanOrEqualTo(4);
            (await dispatcher.DispatchOnceAsync()).Should().Be(0);
        }

        await using (var scope = app.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();
            var rows = await db.OutboxMessages.AsNoTracking().ToListAsync();

            rows.Should().HaveCount(published);
            rows.Should().OnlyContain(row => row.ProcessedAt != null && row.FailedAt == null);
        }

        publisher.Published.Should().Contain(type =>
            type.EndsWith("EventCreatedDomainEvent", StringComparison.Ordinal));
        publisher.Published.Should().Contain(type =>
            type.EndsWith("SeatSoldDomainEvent", StringComparison.Ordinal));
    }

    private sealed class SwitchablePublisher : IIntegrationEventPublisher
    {
        public bool Fail { get; set; } = true;

        public List<string> Published { get; } = [];

        public Task PublishAsync(
            string eventType,
            string payload,
            CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("broker unavailable");
            }

            Published.Add(eventType);
            return Task.CompletedTask;
        }
    }
}
