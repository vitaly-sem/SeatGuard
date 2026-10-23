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
        logger.LogDebug("Integration event payload: {Payload}", payload);

        return Task.CompletedTask;
    }
}
