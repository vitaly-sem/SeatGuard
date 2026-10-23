using MediatR;
using SeatGuard.Application.Contracts;

namespace SeatGuard.Application.Reservations.GetReservation;

public sealed record GetReservationQuery(Guid Id) : IRequest<ReservationDto>;
