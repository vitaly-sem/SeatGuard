using MediatR;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Common;
using SeatGuard.Application.Contracts;
using SeatGuard.Domain.Common;
using SeatGuard.Domain.Events;
using SeatGuard.Domain.Seats;

namespace SeatGuard.Application.Events.CreateEvent;

internal sealed class CreateEventCommandHandler(
    IEventRepository events,
    ISeatRepository seats,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRequestHandler<CreateEventCommand, CreateEventResult>
{
    public async Task<CreateEventResult> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var sections = request.Sections
            .Select(section => new SeatSection(
                section.Name.Trim(),
                section.Rows,
                section.SeatsPerRow,
                Money.Create(section.PriceAmount, section.Currency)))
            .ToList();

        var @event = Event.Create(request.Name, request.Venue, request.StartsAt, sections, now);

        var seatsToCreate = new List<Seat>(@event.SeatCount);
        foreach (var section in sections)
        {
            for (var rowIndex = 0; rowIndex < section.Rows; rowIndex++)
            {
                var row = SeatLabel.RowLabel(rowIndex);

                for (var number = 1; number <= section.SeatsPerRow; number++)
                {
                    seatsToCreate.Add(Seat.Create(@event.Id, section.Name, row, number, section.Price));
                }
            }
        }

        await events.AddAsync(@event, cancellationToken);
        await seats.AddRangeAsync(seatsToCreate, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateEventResult(@event.Id.Value, @event.SeatCount);
    }
}
