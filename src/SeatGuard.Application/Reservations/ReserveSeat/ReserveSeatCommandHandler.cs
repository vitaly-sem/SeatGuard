using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Options;
using SeatGuard.Application.Abstractions.Caching;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Abstractions.Services;
using SeatGuard.Application.Common;
using SeatGuard.Application.Common.Exceptions;
using SeatGuard.Application.Contracts;
using SeatGuard.Application.Observability;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Reservations;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Application.Reservations.ReserveSeat;

internal sealed class ReserveSeatCommandHandler(
    IEventRepository events,
    IReservationRepository reservations,
    ISeatAllocationStrategy allocationStrategy,
    IUnitOfWork unitOfWork,
    IAvailabilityReadModelCache cache,
    IOptions<ReservationSettings> settings,
    TimeProvider timeProvider) : IRequestHandler<ReserveSeatCommand, ReserveSeatResult>
{
    public async Task<ReserveSeatResult> Handle(
        ReserveSeatCommand request,
        CancellationToken cancellationToken)
    {
        var customerId = CustomerId.Create(request.CustomerId);
        var idempotencyKey = NormalizeKey(request.IdempotencyKey);

        if (idempotencyKey is not null)
        {
            var existing = await reservations.FindByIdempotencyKeyAsync(
                customerId,
                idempotencyKey,
                cancellationToken);

            if (existing is not null)
            {
                return new ReserveSeatResult(existing.ToDto(), Replayed: true);
            }
        }

        var eventId = new EventId(request.EventId);
        var @event = await events.FindAsync(eventId, cancellationToken)
            ?? throw new NotFoundException("event", request.EventId);

        var now = timeProvider.GetUtcNow();
        @event.EnsureBookable(now);

        var reservationId = ReservationId.New();
        var seatId = new SeatId(request.SeatId);

        SeatGuardTelemetry.ReservationsInFlight.Add(1);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var seat = await allocationStrategy.AcquireAsync(seatId, reservationId, cancellationToken)
                ?? throw new NotFoundException("seat", request.SeatId);

            if (seat.EventId != eventId)
            {
                throw new NotFoundException("seat", request.SeatId);
            }

            var reservation = Reservation.Create(
                eventId,
                seatId,
                customerId,
                seat.Price,
                settings.Value.HoldDuration,
                now,
                idempotencyKey,
                reservationId);

            await reservations.AddAsync(reservation, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            cache.Remove(AvailabilityCacheKeys.ForEvent(eventId));
            SeatGuardTelemetry.ReservationsCreated.Add(1);

            return new ReserveSeatResult(reservation.ToDto(), Replayed: false);
        }
        catch (Exception exception) when (exception is SeatUnavailableException or ConcurrencyConflictException)
        {
            SeatGuardTelemetry.ReservationConflicts.Add(1);
            throw;
        }
        finally
        {
            SeatGuardTelemetry.ReservationsInFlight.Add(-1);
            SeatGuardTelemetry.ReservationDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private static string? NormalizeKey(string? key)
        => string.IsNullOrWhiteSpace(key) ? null : key.Trim();
}
