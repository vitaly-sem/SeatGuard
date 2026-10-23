using SeatGuard.Domain.Common;

namespace SeatGuard.Infrastructure.Outbox;

/// <summary>
/// Transactional outbox row. Rows are written in the same transaction as the
/// aggregate change, then dispatched at least once by the outbox dispatcher.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    private OutboxMessage()
    {
    }

    public static OutboxMessage FromDomainEvent(IDomainEvent domainEvent, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = domainEvent.GetType().FullName!,
        Payload = System.Text.Json.JsonSerializer.Serialize(
            domainEvent,
            domainEvent.GetType(),
            OutboxJson.Options),
        OccurredAt = domainEvent.OccurredAt,
        NextAttemptAt = now
    };

    /// <summary>
    /// Pushes the next attempt into the future so another dispatcher skips the
    /// row while this one publishes it. The claim itself is committed before any
    /// network call, so no database transaction stays open during publishing.
    /// </summary>
    public void Lease(DateTimeOffset until) => NextAttemptAt = until;

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        LastError = null;
    }

    public void MarkFailed(DateTimeOffset now, string error, int maxAttempts, TimeSpan retryDelay)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        NextAttemptAt = now.Add(retryDelay);

        if (Attempts >= maxAttempts)
        {
            FailedAt = now;
        }
    }
}

internal static class OutboxJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);
}
