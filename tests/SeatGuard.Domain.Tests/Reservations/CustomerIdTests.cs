using FluentAssertions;
using SeatGuard.Domain.Common;
using SeatGuard.Domain.Reservations;

namespace SeatGuard.Domain.Tests.Reservations;

public sealed class CustomerIdTests
{
    [Theory]
    [InlineData("customer-1")]
    [InlineData("CUS_123")]
    [InlineData("a1b")]
    public void Create_accepts_opaque_identifiers(string value)
        => CustomerId.Create(value).Value.Should().Be(value);

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("with space")]
    [InlineData("user@example.com")]
    [InlineData("-leading")]
    public void Create_rejects_values_outside_the_contract(string value)
    {
        var action = () => CustomerId.Create(value);

        action.Should().Throw<DomainValidationException>();
    }
}
