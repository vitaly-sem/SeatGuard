using MediatR;
using SeatGuard.Api.Contracts;
using SeatGuard.Application.Contracts;
using SeatGuard.Application.Reservations.CancelReservation;
using SeatGuard.Application.Reservations.ConfirmReservation;
using SeatGuard.Application.Reservations.GetReservation;
using SeatGuard.Application.Reservations.ReserveSeat;

namespace SeatGuard.Api.Endpoints;

internal static class ReservationEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapReservationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reservations")
            .WithTags("Reservations");

        group.MapPost("/", Reserve)
            .WithName("ReserveSeat")
            .WithSummary("Holds a seat for the configured hold duration")
            .RequireRateLimiting("bookings")
            .Produces<ReservationDto>(StatusCodes.Status201Created)
            .Produces<ReservationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/{id:guid}", GetReservation)
            .WithName("GetReservation")
            .WithSummary("Returns a reservation and its ticket code")
            .Produces<ReservationDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/confirm", Confirm)
            .WithName("ConfirmReservation")
            .WithSummary("Charges the customer and issues a ticket")
            .RequireRateLimiting("bookings")
            .Produces<ReservationDto>()
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone);

        group.MapPost("/{id:guid}/cancel", Cancel)
            .WithName("CancelReservation")
            .WithSummary("Releases the seat and cancels a pending reservation")
            .RequireRateLimiting("bookings")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone);

        return app;
    }

    private static async Task<IResult> Reserve(
        ReserveSeatRequest request,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = httpContext.Request.Headers[IdempotencyHeader].ToString();

        var result = await sender.Send(
            new ReserveSeatCommand(
                request.EventId,
                request.SeatId,
                request.CustomerId,
                string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey),
            cancellationToken);

        if (result.Replayed)
        {
            return Results.Ok(result.Reservation);
        }

        return Results.Created($"/api/v1/reservations/{result.Reservation.Id}", result.Reservation);
    }

    private static async Task<IResult> GetReservation(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetReservationQuery(id), cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> Confirm(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ConfirmReservationCommand(id), cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> Cancel(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new CancelReservationCommand(id), cancellationToken);
        return Results.NoContent();
    }
}
