using System.Globalization;

namespace SeatGuard.Domain.Common;

/// <summary>
/// Immutable monetary value. Amount and currency travel together so that a price
/// can never be compared or summed with a value in another currency.
/// </summary>
public sealed class Money : IEquatable<Money>
{
    private const int MaxScale = 2;

    public decimal Amount { get; }

    public string Currency { get; }

    // Required by EF Core for owned-type materialization.
    private Money()
    {
        Currency = null!;
    }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new DomainValidationException("Amount cannot be negative.");
        }

        if (decimal.Round(amount, MaxScale) != amount)
        {
            throw new DomainValidationException($"Amount cannot have more than {MaxScale} decimal places.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || !currency.All(char.IsAsciiLetter))
        {
            throw new DomainValidationException("Currency must be a three-letter ISO 4217 code.");
        }

        return new Money(amount, currency.ToUpperInvariant());
    }

    public static Money Eur(decimal amount) => Create(amount, "EUR");

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return Create(Amount + other.Amount, Currency);
    }

    public bool Equals(Money? other)
        => other is not null && Amount == other.Amount && string.Equals(Currency, other.Currency, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Money other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new DomainValidationException($"Cannot combine {Currency} with {other.Currency}.");
        }
    }
}
