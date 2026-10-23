using MediatR;
using SeatGuard.Application.Abstractions.Messaging;
using SeatGuard.Application.Contracts;

namespace SeatGuard.Application.Reservations.ReserveSeat;

public sealed record ReserveSeatCommand(
    Guid EventId,
    Guid SeatId,
    string CustomerId,
    string? IdempotencyKey) : IRequest<ReserveSeatResult>, ITransactionalRequest;
