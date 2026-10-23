using FluentValidation;

namespace SeatGuard.Application.Reservations.CancelReservation;

public sealed class CancelReservationCommandValidator : AbstractValidator<CancelReservationCommand>
{
    public CancelReservationCommandValidator()
    {
        RuleFor(command => command.ReservationId).NotEmpty();
    }
}
