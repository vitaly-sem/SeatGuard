namespace SeatGuard.Domain.Common;

/// <summary>
/// Raised when a caller supplies values that cannot form a valid domain object.
/// </summary>
public sealed class DomainValidationException(string message) : DomainException(message);
