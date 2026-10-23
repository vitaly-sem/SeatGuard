namespace SeatGuard.Application.Common.Exceptions;

/// <summary>
/// Raised by the validation behavior. Deliberately separate from
/// FluentValidation.ValidationException so the API layer has one contract.
/// </summary>
public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
