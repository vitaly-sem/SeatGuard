namespace SeatGuard.Application.Common.Exceptions;

public sealed class PaymentDeclinedException(string reason)
    : Exception($"The payment was declined: {reason}")
{
    public string Reason { get; } = reason;
}
