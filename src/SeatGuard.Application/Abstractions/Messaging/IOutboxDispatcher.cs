namespace SeatGuard.Application.Abstractions.Messaging;

/// <summary>
/// Claims due outbox rows, publishes them and records the outcome. Exposed as a
/// single-shot operation so tests can drive it without waiting for the timer.
/// </summary>
public interface IOutboxDispatcher
{
    Task<int> DispatchOnceAsync(CancellationToken cancellationToken = default);
}
