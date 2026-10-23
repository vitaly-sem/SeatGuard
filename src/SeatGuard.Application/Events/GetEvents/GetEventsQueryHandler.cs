using MediatR;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Common;
using SeatGuard.Application.Common.Pagination;
using SeatGuard.Application.Contracts;

namespace SeatGuard.Application.Events.GetEvents;

internal sealed class GetEventsQueryHandler(IEventRepository events)
    : IRequestHandler<GetEventsQuery, PagedResult<EventSummaryDto>>
{
    public async Task<PagedResult<EventSummaryDto>> Handle(
        GetEventsQuery request,
        CancellationToken cancellationToken)
    {
        var (items, totalCount) = await events.ListAsync(request.Page, request.PageSize, cancellationToken);

        return new PagedResult<EventSummaryDto>(
            items.Select(@event => @event.ToSummaryDto()).ToList(),
            request.Page,
            request.PageSize,
            totalCount);
    }
}
