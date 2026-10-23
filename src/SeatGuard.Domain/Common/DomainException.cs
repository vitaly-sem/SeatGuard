namespace SeatGuard.Domain.Common;

/// <summary>
/// Raised when a business invariant is violated. The API layer translates these
/// exceptions into RFC 9457 problem responses with a stable machine-readable code.
/// </summary>
public abstract class DomainException(string message) : Exception(message);
