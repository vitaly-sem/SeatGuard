using Microsoft.Extensions.Logging;
using SeatGuard.Application.Abstractions.Payments;

namespace SeatGuard.Infrastructure.Payments;

/// <summary>
/// Deterministic in-process payment provider used by local runs and tests.
///
/// The reservation id is the provider-side idempotency key: charging the same
/// reservation twice returns the same reference and never moves money twice.
/// Customer ids starting with "declined" exercise the failure path.
/// </summary>
internal sealed class SimulatedPaymentGateway(ILogger<SimulatedPaymentGateway> logger) : IPaymentGateway
{
    private const string DeclinePrefix = "declined";
    private const string Prefix = "pay_";

    public Task<PaymentResult> ChargeAsync(
        PaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CustomerId.Value.StartsWith(DeclinePrefix, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Payment declined for reservation {ReservationId}: simulated decline",
                request.ReservationId);

            return Task.FromResult(PaymentResult.Declined("card_declined"));
        }

        var reference = $"{Prefix}{request.ReservationId.Value:N}";

        logger.LogInformation(
            "Charged {Amount} for reservation {ReservationId} with reference {PaymentReference}",
            request.Amount,
            request.ReservationId,
            reference);

        return Task.FromResult(PaymentResult.Approved(reference));
    }

    public Task RefundAsync(string paymentReference, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Refunded payment {PaymentReference}", paymentReference);
        return Task.CompletedTask;
    }
}
