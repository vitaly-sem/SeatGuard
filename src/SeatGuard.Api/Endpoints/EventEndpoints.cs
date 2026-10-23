using MediatR;
using Microsoft.Extensions.Options;
using SeatGuard.Api.Authentication;
using SeatGuard.Api.Contracts;
using SeatGuard.Application.Common;
using SeatGuard.Application.Common.Pagination;
using SeatGuard.Application.Contracts;
using SeatGuard.Application.Events.CreateEvent;
using SeatGuard.Application.Events.GetAvailability;
using SeatGuard.Application.Events.GetEvent;
using SeatGuard.Application.Events.GetEvents;

namespace SeatGuard.Api.Endpoints;

internal static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events")
            .WithTags("Events");

        group.MapPost("/", CreateEvent)
            .WithName("CreateEvent")
            .WithSummary("Creates an event and generates its seats")
            .RequireAuthorization(ApiKeyAuthenticationHandler.PolicyName)
            .RequireRateLimiting("admin")
            .Produces<CreateEventResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", GetEvents)
            .WithName("GetEvents")
            .WithSummary("Lists events with pagination")
            .Produces<PagedResult<EventSummaryDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", GetEvent)
            .WithName("GetEvent")
            .WithSummary("Returns event details and its seat sections")
            .Produces<EventDetailsDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/availability", GetAvailability)
            .WithName("GetEventAvailability")
            .WithSummary("Returns the availability read model, cached for a short TTL")
            .Produces<EventAvailabilityDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateEvent(
        CreateEventRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateEventCommand(
            request.Name,
            request.Venue,
            request.StartsAt,
            request.Sections
                .Select(section => new SeatSectionInput(
                    section.Name,
                    section.Rows,
                    section.SeatsPerRow,
                    section.PriceAmount,
                    section.Currency))
                .ToList());

        var result = await sender.Send(command, cancellationToken);

        return Results.Created($"/api/v1/events/{result.EventId}", result);
    }

    private static async Task<IResult> GetEvents(
        ISender sender,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        var result = await sender.Send(new GetEventsQuery(page, pageSize), cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetEvent(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetEventQuery(id), cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetAvailability(
        Guid id,
        HttpContext httpContext,
        ISender sender,
        IOptions<ReservationSettings> settings,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetAvailabilityQuery(id), cancellationToken);
        var maxAge = Math.Max(0, (int)settings.Value.AvailabilityCacheTtl.TotalSeconds);

        httpContext.Response.Headers.CacheControl = $"private, max-age={maxAge}";

        return Results.Ok(result);
    }
}
