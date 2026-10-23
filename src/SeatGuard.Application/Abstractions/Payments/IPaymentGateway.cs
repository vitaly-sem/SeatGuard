using SeatGuard.Domain.Common;
using SeatGuard.Domain.Reservations;

namespace SeatGuard.Application.Abstractions.Payments;

public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken = default);

    Task RefundAsync(string paymentReference, CancellationToken cancellationToken = default);
}

public sealed record PaymentRequest(ReservationId ReservationId, CustomerId CustomerId, Money Amount);

public sealed record PaymentResult(bool Succeeded, string? Reference, string? FailureReason)
{
    public static PaymentResult Approved(string reference) => new(true, reference, null);

    public static PaymentResult Declined(string reason) => new(false, null, reason);
}
