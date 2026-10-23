using MediatR;
using SeatGuard.Application.Contracts;

namespace SeatGuard.Application.Reservations.ConfirmReservation;

public sealed record ConfirmReservationCommand(Guid ReservationId) : IRequest<ReservationDto>;
