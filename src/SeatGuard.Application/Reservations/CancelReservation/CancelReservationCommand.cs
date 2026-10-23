using MediatR;

namespace SeatGuard.Application.Reservations.CancelReservation;

public sealed record CancelReservationCommand(Guid ReservationId) : IRequest, Abstractions.Messaging.ITransactionalRequest;
