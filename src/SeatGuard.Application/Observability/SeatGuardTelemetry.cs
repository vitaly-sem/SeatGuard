using System.Diagnostics.Metrics;

namespace SeatGuard.Application.Observability;

/// <summary>
/// Process-wide OpenTelemetry instruments. Counters use a stable prefix so
/// dashboards and alerts can rely on the names.
/// </summary>
public static class SeatGuardTelemetry
{
    public const string MeterName = "SeatGuard.Reservations";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> ReservationsCreated =
        Meter.CreateCounter<long>("seatguard.reservations.created", unit: "{reservation}");

    public static readonly Counter<long> ReservationsConfirmed =
        Meter.CreateCounter<long>("seatguard.reservations.confirmed", unit: "{reservation}");

    public static readonly Counter<long> ReservationsCancelled =
        Meter.CreateCounter<long>("seatguard.reservations.cancelled", unit: "{reservation}");

    public static readonly Counter<long> ReservationsExpired =
        Meter.CreateCounter<long>("seatguard.reservations.expired", unit: "{reservation}");

    public static readonly Counter<long> ReservationConflicts =
        Meter.CreateCounter<long>("seatguard.reservations.conflicts", unit: "{request}");

    public static readonly Histogram<double> ReservationDuration =
        Meter.CreateHistogram<double>("seatguard.reservation.duration", unit: "ms");

    public static readonly UpDownCounter<long> ReservationsInFlight =
        Meter.CreateUpDownCounter<long>("seatguard.reservations.in_flight", unit: "{request}");
}
