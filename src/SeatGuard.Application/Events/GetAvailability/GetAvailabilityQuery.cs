using MediatR;
using SeatGuard.Application.Contracts;

namespace SeatGuard.Application.Events.GetAvailability;

public sealed record GetAvailabilityQuery(Guid EventId) : IRequest<EventAvailabilityDto>;
