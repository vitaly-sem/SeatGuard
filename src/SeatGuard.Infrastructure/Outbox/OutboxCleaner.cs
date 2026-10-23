using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeatGuard.Application.Abstractions.Messaging;
using SeatGuard.Application.Common;
using SeatGuard.Application.Observability;
using SeatGuard.Infrastructure.Persistence;

namespace SeatGuard.Infrastructure.Outbox;

/// <summary>
/// Removes processed outbox rows in bounded batches. The dispatcher only needs
/// recent history for at-least-once delivery; keeping it forever would grow the
/// table and its indexes without bound.
/// </summary>
internal sealed class OutboxCleaner(
    SeatGuardDbContext dbContext,
    IOptions<OutboxSettings> settings,
    TimeProvider timeProvider,
    ILogger<OutboxCleaner> logger) : IOutboxCleaner
{
    public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        var options = settings.Value;

        if (options.RetentionDays <= 0)
        {
            return 0;
        }

        var cutoff = timeProvider.GetUtcNow().AddDays(-options.RetentionDays);
        var total = 0;

        while (true)
        {
            var ids = await dbContext.OutboxMessages
                .AsNoTracking()
                .Where(message => message.ProcessedAt != null && message.ProcessedAt < cutoff)
                .OrderBy(message => message.ProcessedAt)
                .Select(message => message.Id)
                .Take(options.CleanupBatchSize)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0)
            {
                break;
            }

            total += await dbContext.OutboxMessages
                .Where(message => ids.Contains(message.Id))
                .ExecuteDeleteAsync(cancellationToken);

            if (ids.Count < options.CleanupBatchSize)
            {
                break;
            }
        }

        if (total > 0)
        {
            SeatGuardTelemetry.OutboxPurged.Add(total);
            logger.LogInformation(
                "Purged {Count} processed outbox messages older than {Cutoff:O}",
                total,
                cutoff);
        }

        return total;
    }
}
