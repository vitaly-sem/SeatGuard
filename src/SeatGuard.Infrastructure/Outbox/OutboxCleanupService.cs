using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeatGuard.Application.Abstractions.Messaging;
using SeatGuard.Application.Common;

namespace SeatGuard.Infrastructure.Outbox;

/// <summary>
/// Runs outbox retention on a fixed cadence. It waits for the first interval
/// instead of sweeping at startup so a deploy does not issue a large delete
/// while the API is warming up.
/// </summary>
internal sealed class OutboxCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxSettings> settings,
    ILogger<OutboxCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.Value;

        if (!options.CleanupEnabled || options.RetentionDays <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.CleanupInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var cleaner = scope.ServiceProvider.GetRequiredService<IOutboxCleaner>();
                _ = await cleaner.CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox cleanup failed; retrying on the next tick");
            }
        }
    }
}
