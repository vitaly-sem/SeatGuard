namespace SeatGuard.Application.Abstractions.Messaging;

/// <summary>
/// Outbound boundary for integration events. The default implementation writes
/// structured logs; a broker-backed adapter can replace it without touching the
/// application layer.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken = default);
}
