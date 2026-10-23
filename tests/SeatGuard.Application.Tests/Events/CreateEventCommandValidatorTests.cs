using FluentAssertions;
using SeatGuard.Application.Events.CreateEvent;

namespace SeatGuard.Application.Tests.Events;

public sealed class CreateEventCommandValidatorTests
{
    private readonly CreateEventCommandValidator _validator = new(TimeProvider.System);

    [Fact]
    public void Valid_command_passes()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Past_start_date_is_rejected()
    {
        var result = _validator.Validate(ValidCommand() with { StartsAt = DateTimeOffset.UtcNow.AddDays(-1) });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateEventCommand.StartsAt));
    }

    [Fact]
    public void Empty_sections_are_rejected()
    {
        var result = _validator.Validate(ValidCommand() with { Sections = [] });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateEventCommand.Sections));
    }

    [Fact]
    public void Total_seat_limit_is_enforced()
    {
        var sections = Enumerable.Range(0, 11)
            .Select(index => new SeatSectionInput($"S{index}", 100, 100, 10, "EUR"))
            .ToList();

        var result = _validator.Validate(ValidCommand() with { Sections = sections });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("10000 seats", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public void Invalid_currency_is_rejected(string currency)
    {
        var command = ValidCommand() with
        {
            Sections = [new SeatSectionInput("Orchestra", 2, 10, 89, currency)]
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Price_with_more_than_two_decimals_is_rejected()
    {
        var command = ValidCommand() with
        {
            Sections = [new SeatSectionInput("Orchestra", 2, 10, 89.999m, "EUR")]
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
    }

    private static CreateEventCommand ValidCommand() => new(
        "Symphony No. 7",
        "Konzerthaus Berlin",
        DateTimeOffset.UtcNow.AddDays(30),
        [new SeatSectionInput("Orchestra", 10, 20, 89, "EUR")]);
}
