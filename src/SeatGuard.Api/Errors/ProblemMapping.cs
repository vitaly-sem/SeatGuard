using SeatGuard.Application.Common.Exceptions;
using SeatGuard.Domain.Common;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Reservations;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Api.Errors;

internal static class ProblemMapping
{
    public static (int Status, string Code, string Title, string Detail) Map(Exception exception) => exception switch
    {
        RequestValidationException => (
            StatusCodes.Status400BadRequest,
            "request.invalid",
            "Request validation failed",
            "One or more fields are invalid."),

        DomainValidationException domain => (
            StatusCodes.Status422UnprocessableEntity,
            "domain.invalid",
            "Business rule violated",
            domain.Message),

        NotFoundException notFound => (
            StatusCodes.Status404NotFound,
            "resource.not_found",
            "Resource not found",
            notFound.Message),

        SeatUnavailableException seat => (
            StatusCodes.Status409Conflict,
            "seat.not_available",
            "Seat is not available",
            seat.Message),

        InvalidSeatStateException seatState => (
            StatusCodes.Status409Conflict,
            "seat.invalid_state",
            "Seat state conflict",
            seatState.Message),

        ReservationExpiredException expired => (
            StatusCodes.Status410Gone,
            "reservation.expired",
            "Reservation expired",
            expired.Message),

        InvalidReservationStateException reservationState => (
            StatusCodes.Status409Conflict,
            "reservation.invalid_state",
            "Reservation state conflict",
            reservationState.Message),

        EventNotOnSaleException notOnSale => (
            StatusCodes.Status409Conflict,
            "event.not_on_sale",
            "Event is not on sale",
            notOnSale.Message),

        PaymentDeclinedException declined => (
            StatusCodes.Status402PaymentRequired,
            "payment.declined",
            "Payment declined",
            declined.Message),

        DuplicateIdempotencyKeyException => (
            StatusCodes.Status409Conflict,
            "idempotency.key_in_progress",
            "Idempotency key already in use",
            "A request with the same idempotency key is currently being processed. Retry shortly."),

        ConcurrencyConflictException conflict => (
            StatusCodes.Status409Conflict,
            "seat.conflict",
            "Concurrent update conflict",
            conflict.Message),

        _ => (
            StatusCodes.Status500InternalServerError,
            "server.error",
            "Unexpected error",
            "The request could not be completed. Use the trace id when contacting support.")
    };
}
