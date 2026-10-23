using MediatR;
using SeatGuard.Application.Common.Pagination;
using SeatGuard.Application.Contracts;

namespace SeatGuard.Application.Events.GetEvents;

public sealed record GetEventsQuery(int Page = 1, int PageSize = 20) : IRequest<PagedResult<EventSummaryDto>>;
