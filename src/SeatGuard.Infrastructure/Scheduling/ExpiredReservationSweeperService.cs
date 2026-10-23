using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeatGuard.Application.Abstractions.Services;
using SeatGuard.Application.Common;

namespace SeatGuard.Infrastructure.Scheduling;

/// <summary>
/// Runs the sweeper on a fixed cadence. Each tick creates its own scope so the
/// DbContext is not shared with request processing.
/// </summary>
internal sealed class ExpiredReservationSweeperService(
    IServiceScopeFactory scopeFactory,
    IOptions<ReservationSettings> settings,
    ILogger<ExpiredReservationSweeperService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.Value.SweeperInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<IExpiredReservationSweeper>();
                await sweeper.SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A failed batch is retried on the next tick; never let it kill the loop.
                logger.LogError(exception, "Expired reservation sweep failed");
            }
        }
    }
}
