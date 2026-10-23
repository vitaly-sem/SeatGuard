using FluentValidation;

namespace SeatGuard.Application.Events.CreateEvent;

public sealed class SeatSectionInputValidator : AbstractValidator<SeatSectionInput>
{
    public SeatSectionInputValidator()
    {
        RuleFor(section => section.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(section => section.Rows)
            .InclusiveBetween(1, 200);

        RuleFor(section => section.SeatsPerRow)
            .InclusiveBetween(1, 200);

        RuleFor(section => section.PriceAmount)
            .GreaterThanOrEqualTo(0)
            .Must(amount => decimal.Round(amount, 2) == amount)
            .WithMessage("Price amount cannot have more than 2 decimal places.");

        RuleFor(section => section.Currency)
            .NotEmpty()
            .Length(3)
            .Matches("^[A-Za-z]{3}$")
            .WithMessage("Currency must be a three-letter ISO 4217 code.");
    }
}
