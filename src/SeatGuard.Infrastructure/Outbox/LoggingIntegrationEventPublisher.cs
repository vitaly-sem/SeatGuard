using Microsoft.Extensions.Logging;
using SeatGuard.Application.Abstractions.Messaging;

namespace SeatGuard.Infrastructure.Outbox;

/// <summary>
/// Default publisher: writes a structured log line. Swap this adapter for a
/// broker-backed implementation (Kafka, SNS, Service Bus) without changing the
/// application layer.
/// </summary>
internal sealed class LoggingIntegrationEventPublisher(ILogger<LoggingIntegrationEventPublisher> logger)
    : IIntegrationEventPublisher
{
    public Task PublishAsync(
        string eventType,
        string payload,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Publishing integration event {EventType}", eventType);

        // The payload carries customer identifiers and ticket codes. Log only its
        // size so production logs cannot leak personal data.
        logger.LogDebug(
            "Integration event {EventType} ready ({PayloadBytes} bytes)",
            eventType,
            payload.Length);

        return Task.CompletedTask;
    }
}
