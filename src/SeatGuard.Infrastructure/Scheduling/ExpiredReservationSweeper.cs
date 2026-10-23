using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeatGuard.Application.Abstractions.Caching;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Abstractions.Services;
using SeatGuard.Application.Common;
using SeatGuard.Application.Observability;
using SeatGuard.Infrastructure.Persistence;

namespace SeatGuard.Infrastructure.Scheduling;

/// <summary>
/// Expires pending reservations in batches.
///
/// The claim query uses FOR UPDATE SKIP LOCKED: several API replicas can sweep
/// concurrently without blocking each other, and each expired row is processed
/// by exactly one worker for the duration of the transaction. Confirm and cancel
/// take a plain FOR UPDATE on the same row, so they wait for the sweeper and then
/// observe the final state instead of racing it.
/// </summary>
internal sealed class ExpiredReservationSweeper(
    SeatGuardDbContext dbContext,
    IUnitOfWork unitOfWork,
    IAvailabilityReadModelCache cache,
    IOptions<ReservationSettings> settings,
    TimeProvider timeProvider,
    ILogger<ExpiredReservationSweeper> logger) : IExpiredReservationSweeper
{
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var batchSize = settings.Value.SweeperBatchSize;

        var expiredCount = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var candidates = await dbContext.Reservations
                    .FromSqlInterpolated(
                        $"SELECT * FROM reservations WHERE status = 'Pending' AND expires_at <= {now} ORDER BY expires_at LIMIT {batchSize} FOR UPDATE SKIP LOCKED")
                    .ToListAsync(token);

                if (candidates.Count == 0)
                {
                    return 0;
                }

                var expired = 0;

                foreach (var reservation in candidates)
                {
                    if (!reservation.Expire(now))
                    {
                        continue;
                    }

                    var seat = await dbContext.Seats
                        .SingleOrDefaultAsync(candidate => candidate.Id == reservation.SeatId, token);

                    if (seat is null)
                    {
                        logger.LogWarning(
                            "Reservation {ReservationId} references missing seat {SeatId}",
                            reservation.Id,
                            reservation.SeatId);
                    }
                    else if (seat.HeldByReservationId == reservation.Id)
                    {
                        seat.Release(reservation.Id);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Seat {SeatId} is not held by expired reservation {ReservationId}; leaving it untouched",
                            seat.Id,
                            reservation.Id);
                    }

                    cache.Remove(AvailabilityCacheKeys.ForEvent(reservation.EventId));
                    expired++;
                }

                await unitOfWork.SaveChangesAsync(token);

                return expired;
            },
            cancellationToken);

        if (expiredCount > 0)
        {
            SeatGuardTelemetry.ReservationsExpired.Add(expiredCount);
            logger.LogInformation("Expired {Count} reservations", expiredCount);
        }

        return expiredCount;
    }
}
