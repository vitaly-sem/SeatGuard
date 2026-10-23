using MediatR;
using SeatGuard.Application.Abstractions.Caching;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Common.Exceptions;
using SeatGuard.Application.Observability;
using SeatGuard.Domain.Reservations;

namespace SeatGuard.Application.Reservations.CancelReservation;

internal sealed class CancelReservationCommandHandler(
    IReservationRepository reservations,
    ISeatRepository seats,
    IUnitOfWork unitOfWork,
    IAvailabilityReadModelCache cache,
    TimeProvider timeProvider) : IRequestHandler<CancelReservationCommand>
{
    public async Task Handle(CancelReservationCommand request, CancellationToken cancellationToken)
    {
        var reservationId = new ReservationId(request.ReservationId);
        var now = timeProvider.GetUtcNow();

        var reservation = await reservations.FindForUpdateAsync(reservationId, cancellationToken)
            ?? throw new NotFoundException("reservation", request.ReservationId);

        if (reservation.Status == ReservationStatus.Cancelled)
        {
            return;
        }

        if (reservation.Status == ReservationStatus.Expired)
        {
            throw new ReservationExpiredException(reservation.Id, reservation.ExpiresAt);
        }

        if (!reservation.Cancel(now))
        {
            return;
        }

        var seat = await seats.FindForUpdateAsync(reservation.SeatId, cancellationToken)
            ?? throw new NotFoundException("seat", reservation.SeatId.Value);

        seat.Release(reservation.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        cache.Remove(AvailabilityCacheKeys.ForEvent(reservation.EventId));
        SeatGuardTelemetry.ReservationsCancelled.Add(1);
    }
}
