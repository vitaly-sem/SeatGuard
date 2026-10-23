using FluentValidation;

namespace SeatGuard.Application.Reservations.ConfirmReservation;

public sealed class ConfirmReservationCommandValidator : AbstractValidator<ConfirmReservationCommand>
{
    public ConfirmReservationCommandValidator()
    {
        RuleFor(command => command.ReservationId).NotEmpty();
    }
}
