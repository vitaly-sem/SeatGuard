using MediatR;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Common;
using SeatGuard.Application.Common.Exceptions;
using SeatGuard.Application.Contracts;
using SeatGuard.Domain.Reservations;

namespace SeatGuard.Application.Reservations.GetReservation;

internal sealed class GetReservationQueryHandler(IReservationRepository reservations)
    : IRequestHandler<GetReservationQuery, ReservationDto>
{
    public async Task<ReservationDto> Handle(GetReservationQuery request, CancellationToken cancellationToken)
    {
        var reservation = await reservations.FindAsync(new ReservationId(request.Id), cancellationToken)
            ?? throw new NotFoundException("reservation", request.Id);

        return reservation.ToDto();
    }
}
