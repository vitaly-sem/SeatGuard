namespace SeatGuard.Application.Common.Exceptions;

/// <summary>
/// A concurrent request with the same idempotency key is still being processed.
/// The client should retry and will then receive the stored response.
/// </summary>
public sealed class DuplicateIdempotencyKeyException(string? idempotencyKey = null)
    : Exception(idempotencyKey is null
        ? "A request with the same idempotency key is already being processed."
        : $"A request with idempotency key '{idempotencyKey}' is already being processed.");
