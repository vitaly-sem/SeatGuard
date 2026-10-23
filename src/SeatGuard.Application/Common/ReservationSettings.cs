namespace SeatGuard.Application.Common;

/// <summary>
/// Reservation behaviour configured under the "Reservations" section.
/// </summary>
public sealed class ReservationSettings
{
    public const string SectionName = "Reservations";

    public TimeSpan HoldDuration { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan AvailabilityCacheTtl { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan SweeperInterval { get; set; } = TimeSpan.FromSeconds(15);

    public int SweeperBatchSize { get; set; } = 100;

    public bool SweeperEnabled { get; set; } = true;

    public string ConcurrencyStrategy { get; set; } = "Pessimistic";
}
