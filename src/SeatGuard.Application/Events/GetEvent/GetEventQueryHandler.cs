using MediatR;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Common;
using SeatGuard.Application.Common.Exceptions;
using SeatGuard.Application.Contracts;
using SeatGuard.Domain.Events;

namespace SeatGuard.Application.Events.GetEvent;

internal sealed class GetEventQueryHandler(IEventRepository events)
    : IRequestHandler<GetEventQuery, EventDetailsDto>
{
    public async Task<EventDetailsDto> Handle(GetEventQuery request, CancellationToken cancellationToken)
    {
        var id = new EventId(request.Id);
        var @event = await events.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("event", request.Id);

        return @event.ToDetailsDto();
    }
}
