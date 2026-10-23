using MediatR;
using Microsoft.Extensions.Options;
using SeatGuard.Application.Abstractions.Caching;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Common;
using SeatGuard.Application.Common.Exceptions;
using SeatGuard.Application.Contracts;
using SeatGuard.Domain.Events;

namespace SeatGuard.Application.Events.GetAvailability;

internal sealed class GetAvailabilityQueryHandler(
    IEventRepository events,
    ISeatRepository seatRepository,
    IAvailabilityReadModelCache cache,
    IOptions<ReservationSettings> settings,
    TimeProvider timeProvider) : IRequestHandler<GetAvailabilityQuery, EventAvailabilityDto>
{
    public async Task<EventAvailabilityDto> Handle(
        GetAvailabilityQuery request,
        CancellationToken cancellationToken)
    {
        var eventId = new EventId(request.EventId);
        var cacheKey = AvailabilityCacheKeys.ForEvent(eventId);

        if (cache.TryGet<EventAvailabilityDto>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var @event = await events.FindAsync(eventId, cancellationToken)
            ?? throw new NotFoundException("event", request.EventId);

        var seats = await seatRepository.ListByEventAsync(eventId, cancellationToken);

        var availability = new EventAvailabilityDto(
            @event.Id.Value,
            @event.Name,
            @event.StartsAt,
            timeProvider.GetUtcNow(),
            seats.Select(seat => seat.ToAvailabilityDto()).ToList());

        cache.Store(cacheKey, availability, settings.Value.AvailabilityCacheTtl);

        return availability;
    }
}
