using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeatGuard.Application.Abstractions.Messaging;
using SeatGuard.Application.Common;

namespace SeatGuard.Infrastructure.Outbox;

/// <summary>
/// Runs the outbox dispatcher on a fixed cadence with its own DI scope.
/// </summary>
internal sealed class OutboxDispatcherService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxSettings> settings,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.Value.PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
                var published = await dispatcher.DispatchOnceAsync(stoppingToken);

                if (published > 0)
                {
                    logger.LogInformation("Published {Count} outbox messages", published);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatch failed; retrying on the next tick");
            }
        }
    }
}
