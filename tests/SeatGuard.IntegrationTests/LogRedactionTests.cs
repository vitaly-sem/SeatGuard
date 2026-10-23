using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SeatGuard.Application.Abstractions.Messaging;
using SeatGuard.IntegrationTests.Infrastructure;

namespace SeatGuard.IntegrationTests;

[Collection(IntegrationSuite.Name)]
public sealed class LogRedactionTests
{
    private readonly SeatGuardFixture _fixture;

    public LogRedactionTests(SeatGuardFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task OutboxPublisher_DoesNotLogCustomerIdentifiersOrTicketCodes()
    {
        var messages = new ConcurrentQueue<string>();

        await using var app = await _fixture.StartAppAsync(configureServices: services =>
        {
            services.AddSingleton<ILoggerProvider>(new CapturingLoggerProvider(messages));
            services.Configure<LoggerFilterOptions>(options =>
            {
                options.MinLevel = LogLevel.Debug;
                options.Rules.Clear();
            });
        });

        var client = app.Client;
        var customerId = $"customer-secret-{Guid.NewGuid():N}";

        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);
        var seatId = (await client.GetAvailabilityAsync(eventId)).SeatId("Orchestra · A1");
        var (reserve, reservation) = await client.ReserveAsync(eventId, seatId, customerId);
        reserve.StatusCode.Should().Be(HttpStatusCode.Created);

        var reservationId = reservation.GetProperty("id").GetGuid();
        var confirm = await client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", null);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        var ticket = (await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticketCode").GetString();

        await using (var scope = app.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
            (await dispatcher.DispatchOnceAsync()).Should().BeGreaterThan(0);
        }

        var captured = messages.ToArray();

        captured.Should().Contain(message => message.Contains("Publishing integration event", StringComparison.Ordinal));
        captured.Should().Contain(message => message.Contains("Integration event", StringComparison.Ordinal)
            && message.Contains("bytes", StringComparison.Ordinal));

        captured.Should().NotContain(message => message.Contains(customerId, StringComparison.Ordinal));
        captured.Should().NotContain(message => message.Contains(ticket!, StringComparison.Ordinal));
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string categoryName, ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    messages.Enqueue($"[{logLevel}] {categoryName}: {formatter(state, exception)}");
                }
            }
        }
    }
}
