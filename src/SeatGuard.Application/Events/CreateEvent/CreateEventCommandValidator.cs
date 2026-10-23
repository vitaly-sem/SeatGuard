using FluentValidation;

namespace SeatGuard.Application.Events.CreateEvent;

public sealed class CreateEventCommandValidator : AbstractValidator<CreateEventCommand>
{
    public CreateEventCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(command => command.Venue)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(command => command.StartsAt)
            .Must(startsAt => startsAt > timeProvider.GetUtcNow())
            .WithMessage("The event must start in the future.");

        RuleFor(command => command.Sections)
            .NotEmpty()
            .Must(sections => sections.Count <= 50)
            .WithMessage("An event cannot have more than 50 sections.");

        RuleForEach(command => command.Sections)
            .SetValidator(new SeatSectionInputValidator());

        RuleFor(command => command.Sections)
            .Must(sections => sections.Sum(section => (long)section.Rows * section.SeatsPerRow) <= 10_000)
            .WithMessage("An event cannot contain more than 10000 seats.")
            .When(command => command.Sections is { Count: > 0 });
    }
}
