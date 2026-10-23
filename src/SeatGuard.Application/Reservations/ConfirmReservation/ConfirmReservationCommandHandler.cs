using MediatR;
using SeatGuard.Application.Abstractions.Caching;
using SeatGuard.Application.Abstractions.Payments;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Common;
using SeatGuard.Application.Common.Exceptions;
using SeatGuard.Application.Contracts;
using SeatGuard.Application.Observability;
using SeatGuard.Domain.Reservations;

namespace SeatGuard.Application.Reservations.ConfirmReservation;

/// <summary>
/// Confirms a hold and charges the customer.
///
/// The charge happens before the database transaction so that no network call is
/// made while a row lock is held; the payment provider is called with the
/// reservation id as its idempotency key. If the transaction then fails, the
/// charge is refunded (compensation). This is the simplest correct flow for an
/// in-process gateway; ADR-0007 documents the outbox/saga variant for a remote PSP.
/// </summary>
internal sealed class ConfirmReservationCommandHandler(
    IReservationRepository reservations,
    ISeatRepository seats,
    IPaymentGateway paymentGateway,
    IUnitOfWork unitOfWork,
    IAvailabilityReadModelCache cache,
    TimeProvider timeProvider) : IRequestHandler<ConfirmReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(
        ConfirmReservationCommand request,
        CancellationToken cancellationToken)
    {
        var reservationId = new ReservationId(request.ReservationId);

        var reservation = await reservations.FindAsync(reservationId, cancellationToken)
            ?? throw new NotFoundException("reservation", request.ReservationId);

        if (reservation.Status == ReservationStatus.Confirmed)
        {
            return reservation.ToDto();
        }

        if (reservation.Status == ReservationStatus.Expired)
        {
            throw new ReservationExpiredException(reservation.Id, reservation.ExpiresAt);
        }

        if (reservation.Status != ReservationStatus.Pending)
        {
            throw new InvalidReservationStateException(
                reservation.Id,
                reservation.Status,
                "only pending reservations can be confirmed.");
        }

        if (reservation.IsExpired(timeProvider.GetUtcNow()))
        {
            throw new ReservationExpiredException(reservation.Id, reservation.ExpiresAt);
        }

        var payment = await paymentGateway.ChargeAsync(
            new PaymentRequest(reservation.Id, reservation.CustomerId, reservation.Amount),
            cancellationToken);

        if (!payment.Succeeded || string.IsNullOrEmpty(payment.Reference))
        {
            throw new PaymentDeclinedException(payment.FailureReason ?? "unknown reason");
        }

        try
        {
            var confirmed = await ConfirmAndSellAsync(reservationId, payment.Reference, cancellationToken);
            SeatGuardTelemetry.ReservationsConfirmed.Add(1);
            return confirmed;
        }
        catch
        {
            // Compensation: the customer must not be charged for a seat we failed
            // to confirm. CancellationToken.None keeps the refund alive on shutdown.
            await paymentGateway.RefundAsync(payment.Reference, CancellationToken.None);
            throw;
        }
    }

    private async Task<ReservationDto> ConfirmAndSellAsync(
        ReservationId reservationId,
        string paymentReference,
        CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // The clock is read again inside the transaction: the payment call
                // above can outlive the hold, and a timestamp captured before it
                // would confirm an expired reservation.
                var confirmedAt = timeProvider.GetUtcNow();

                // The row lock is the only source of truth. The preflight read is
                // untracked, so this query materialises the row's current state
                // instead of returning the stale snapshot taken before payment.
                var reservation = await reservations.FindForUpdateAsync(reservationId, token)
                    ?? throw new NotFoundException("reservation", reservationId.Value);

                if (reservation.Status == ReservationStatus.Confirmed)
                {
                    return reservation.ToDto();
                }

                if (reservation.Status == ReservationStatus.Expired
                    || reservation.IsExpired(confirmedAt))
                {
                    throw new ReservationExpiredException(reservation.Id, reservation.ExpiresAt);
                }

                var seat = await seats.FindForUpdateAsync(reservation.SeatId, token)
                    ?? throw new NotFoundException("seat", reservation.SeatId.Value);

                reservation.Confirm(confirmedAt, paymentReference);
                seat.Sell(reservation.Id, confirmedAt);

                await unitOfWork.SaveChangesAsync(token);
                cache.Remove(AvailabilityCacheKeys.ForEvent(reservation.EventId));

                return reservation.ToDto();
            },
            cancellationToken);
    }
}
