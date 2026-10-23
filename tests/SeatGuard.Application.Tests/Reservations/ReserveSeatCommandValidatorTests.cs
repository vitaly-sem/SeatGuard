using FluentAssertions;
using SeatGuard.Application.Reservations.ReserveSeat;

namespace SeatGuard.Application.Tests.Reservations;

public sealed class ReserveSeatCommandValidatorTests
{
    private readonly ReserveSeatCommandValidator _validator = new();

    [Fact]
    public void Valid_command_passes()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("user@example.com")]
    public void Invalid_customer_id_is_rejected(string customerId)
    {
        var result = _validator.Validate(ValidCommand() with { CustomerId = customerId });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(ReserveSeatCommand.CustomerId));
    }

    [Fact]
    public void Overlong_idempotency_key_is_rejected()
    {
        var result = _validator.Validate(ValidCommand() with { IdempotencyKey = new string('k', 129) });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(ReserveSeatCommand.IdempotencyKey));
    }

    [Fact]
    public void Empty_ids_are_rejected()
    {
        var result = _validator.Validate(ValidCommand() with { EventId = Guid.Empty, SeatId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(ReserveSeatCommand.EventId));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(ReserveSeatCommand.SeatId));
    }

    private static ReserveSeatCommand ValidCommand()
        => new(Guid.NewGuid(), Guid.NewGuid(), "customer-1", "key-1");
}
