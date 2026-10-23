namespace SeatGuard.Application.Common.Exceptions;

/// <summary>
/// A concurrent request with the same idempotency key is still being processed.
/// The client should retry and will then receive the stored response.
/// </summary>
public sealed class DuplicateIdempotencyKeyException(string idempotencyKey)
    : Exception($"A request with idempotency key '{idempotencyKey}' is already being processed.");
