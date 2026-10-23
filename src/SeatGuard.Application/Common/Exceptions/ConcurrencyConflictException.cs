namespace SeatGuard.Application.Common.Exceptions;

/// <summary>
/// The optimistic concurrency check lost a race. For seat allocation this is a
/// normal outcome and means the seat was taken by another request.
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
