using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeatGuard.Application.Abstractions.Messaging;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Common;
using SeatGuard.Infrastructure.Persistence;

namespace SeatGuard.Infrastructure.Outbox;

/// <summary>
/// Dispatches outbox rows with a lease-based claim:
///
/// 1. Claim due rows inside a short transaction using FOR UPDATE SKIP LOCKED and
///    push next_attempt_at into the future (the lease), then commit.
/// 2. Publish outside any transaction.
/// 3. Mark the row processed or schedule a retry with exponential backoff.
///
/// If the process dies between 2 and 3 the lease expires and the row is delivered
/// again. Delivery is therefore at-least-once and consumers must be idempotent.
/// </summary>
internal sealed class OutboxDispatcher(
    SeatGuardDbContext dbContext,
    IUnitOfWork unitOfWork,
    IIntegrationEventPublisher publisher,
    IOptions<OutboxSettings> settings,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : IOutboxDispatcher
{
    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var options = settings.Value;

        var claimed = await ClaimAsync(now, options, cancellationToken);

        if (claimed.Count == 0)
        {
            return 0;
        }

        var published = 0;

        foreach (var message in claimed)
        {
            try
            {
                await publisher.PublishAsync(message.Type, message.Payload, cancellationToken);

                message.MarkProcessed(timeProvider.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);
                published++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                message.MarkFailed(
                    timeProvider.GetUtcNow(),
                    exception.Message,
                    options.MaxAttempts,
                    ComputeBackoff(message.Attempts));

                await unitOfWork.SaveChangesAsync(CancellationToken.None);

                logger.LogError(
                    exception,
                    "Failed to publish outbox message {MessageId} of type {EventType} (attempt {Attempts}/{MaxAttempts})",
                    message.Id,
                    message.Type,
                    message.Attempts,
                    options.MaxAttempts);
            }
        }

        return published;
    }

    private Task<List<OutboxMessage>> ClaimAsync(
        DateTimeOffset now,
        OutboxSettings options,
        CancellationToken cancellationToken)
        => unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var batch = await dbContext.OutboxMessages
                    .FromSqlInterpolated(
                        $"SELECT * FROM outbox_messages WHERE processed_at IS NULL AND failed_at IS NULL AND next_attempt_at <= {now} ORDER BY occurred_at LIMIT {options.BatchSize} FOR UPDATE SKIP LOCKED")
                    .ToListAsync(token);

                var leaseUntil = now.Add(options.LeaseDuration);
                foreach (var message in batch)
                {
                    message.Lease(leaseUntil);
                }

                await unitOfWork.SaveChangesAsync(token);

                return batch;
            },
            cancellationToken);

    private static TimeSpan ComputeBackoff(int attempts)
    {
        var seconds = Math.Min(300, Math.Pow(2, attempts + 1));
        return TimeSpan.FromSeconds(seconds);
    }
}
