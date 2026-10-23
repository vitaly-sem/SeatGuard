using FluentValidation;
using SeatGuard.Domain.Reservations;

namespace SeatGuard.Application.Reservations.ReserveSeat;

public sealed class ReserveSeatCommandValidator : AbstractValidator<ReserveSeatCommand>
{
    public ReserveSeatCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.SeatId).NotEmpty();

        RuleFor(command => command.CustomerId)
            .NotEmpty()
            .Must(customerId => CustomerId.TryCreate(customerId, out _))
            .WithMessage(
                "Customer id must be 3-64 characters of letters, digits, dots, dashes or underscores.");

        RuleFor(command => command.IdempotencyKey)
            .MaximumLength(Reservation.MaxIdempotencyKeyLength)
            .When(command => command.IdempotencyKey is not null);
    }
}
