namespace SeatGuard.Application.Abstractions.Messaging;

/// <summary>
/// Deletes processed outbox rows after their retention window. Failed and
/// pending rows are never removed automatically so they stay available for
/// diagnosis and replay.
/// </summary>
public interface IOutboxCleaner
{
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);
}
