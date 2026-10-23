using System.Text.RegularExpressions;
using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Reservations;

/// <summary>
/// Opaque customer identifier supplied by the caller's system. It is deliberately
/// not an email address so that the booking service stores no personal data.
/// </summary>
public readonly partial record struct CustomerId(string Value)
{
    public static CustomerId Create(string? value)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrEmpty(normalized) || !Pattern().IsMatch(normalized))
        {
            throw new DomainValidationException(
                "Customer id must be 3-64 characters of letters, digits, dots, dashes or underscores.");
        }

        return new CustomerId(normalized);
    }

    public static bool TryCreate(string? value, out CustomerId customerId)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrEmpty(normalized) || !Pattern().IsMatch(normalized))
        {
            customerId = default;
            return false;
        }

        customerId = new CustomerId(normalized);
        return true;
    }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
