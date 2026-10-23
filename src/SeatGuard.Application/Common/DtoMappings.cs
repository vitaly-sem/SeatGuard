using SeatGuard.Application.Contracts;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Reservations;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Application.Common;

internal static class DtoMappings
{
    public static EventSummaryDto ToSummaryDto(this Event @event) => new(
        @event.Id.Value,
        @event.Name,
        @event.Venue,
        @event.StartsAt,
        @event.Status.ToString(),
        @event.SeatCount);

    public static EventDetailsDto ToDetailsDto(this Event @event) => new(
        @event.Id.Value,
        @event.Name,
        @event.Venue,
        @event.StartsAt,
        @event.Status.ToString(),
        @event.SeatCount,
        @event.Sections
            .Select(section => new EventSectionDto(
                section.Name,
                section.Rows,
                section.SeatsPerRow,
                section.Price.Amount,
                section.Price.Currency))
            .ToList());

    public static ReservationDto ToDto(this Reservation reservation) => new(
        reservation.Id.Value,
        reservation.EventId.Value,
        reservation.SeatId.Value,
        reservation.CustomerId.Value,
        reservation.Status.ToString(),
        reservation.Amount.Amount,
        reservation.Amount.Currency,
        reservation.CreatedAt,
        reservation.ExpiresAt,
        reservation.ConfirmedAt,
        reservation.TicketCode);

    public static SeatAvailabilityDto ToAvailabilityDto(this Seat seat) => new(
        seat.Id.Value,
        seat.Label,
        seat.Section,
        seat.Row,
        seat.Number,
        seat.Status.ToString(),
        seat.Price.Amount,
        seat.Price.Currency);
}
