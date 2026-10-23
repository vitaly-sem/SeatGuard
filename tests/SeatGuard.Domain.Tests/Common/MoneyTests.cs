using FluentAssertions;
using SeatGuard.Domain.Common;

namespace SeatGuard.Domain.Tests.Common;

public sealed class MoneyTests
{
    [Fact]
    public void Create_normalizes_currency_and_preserves_scale()
    {
        var money = Money.Create(89.5m, "eur");

        money.Amount.Should().Be(89.5m);
        money.Currency.Should().Be("EUR");
        money.ToString().Should().Be("89.50 EUR");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(10.001)]
    public void Create_rejects_negative_or_over_precise_amounts(decimal amount)
    {
        var action = () => Money.Create(amount, "EUR");

        action.Should().Throw<DomainValidationException>();
    }

    [Theory]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("12A")]
    public void Create_rejects_invalid_currency_codes(string currency)
    {
        var action = () => Money.Create(10, currency);

        action.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Equality_is_based_on_amount_and_currency()
    {
        Money.Create(10, "EUR").Should().Be(Money.Create(10, "eur"));
        Money.Create(10, "EUR").Should().NotBe(Money.Create(10, "USD"));
    }

    [Fact]
    public void Add_requires_the_same_currency()
    {
        var eur = Money.Eur(10);

        eur.Add(Money.Eur(5)).Amount.Should().Be(15);

        var action = () => eur.Add(Money.Create(5, "USD"));
        action.Should().Throw<DomainValidationException>().WithMessage("*EUR*USD*");
    }
}
