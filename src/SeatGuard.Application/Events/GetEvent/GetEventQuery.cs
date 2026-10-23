using MediatR;
using SeatGuard.Application.Contracts;

namespace SeatGuard.Application.Events.GetEvent;

public sealed record GetEventQuery(Guid Id) : IRequest<EventDetailsDto>;
