using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SeatGuard.Application.Common.Behaviors;
using SeatGuard.Application.Events.CreateEvent;

namespace SeatGuard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssemblyContaining<CreateEventCommand>();

            // Order matters: logging wraps validation, validation wraps the transaction.
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssemblyContaining<CreateEventCommand>(includeInternalTypes: true);
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
