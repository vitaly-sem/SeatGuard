namespace SeatGuard.Application.Abstractions.Services;

/// <summary>
/// Releases holds whose TTL elapsed. Exposed as a single-shot operation so tests
/// can drive it deterministically while the hosted service runs it on a timer.
/// </summary>
public interface IExpiredReservationSweeper
{
    Task<int> SweepOnceAsync(CancellationToken cancellationToken = default);
}
