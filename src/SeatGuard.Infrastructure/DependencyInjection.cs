using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SeatGuard.Application.Abstractions.Caching;
using SeatGuard.Application.Abstractions.Payments;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Abstractions.Services;
using SeatGuard.Application.Common;
using SeatGuard.Infrastructure.Caching;
using SeatGuard.Infrastructure.Concurrency;
using SeatGuard.Infrastructure.Payments;
using SeatGuard.Infrastructure.Persistence;
using SeatGuard.Infrastructure.Persistence.Repositories;
using SeatGuard.Infrastructure.Scheduling;

namespace SeatGuard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SeatGuard")
            ?? throw new InvalidOperationException(
                "Connection string 'SeatGuard' is not configured.");

        services.AddDbContext<SeatGuardDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.CommandTimeout(30))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<ISeatRepository, SeatRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();

        services.AddMemoryCache();
        services.AddSingleton<IAvailabilityReadModelCache, MemoryAvailabilityReadModelCache>();
        services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();

        services.AddScoped<IExpiredReservationSweeper, ExpiredReservationSweeper>();
        if (configuration.GetValue("Reservations:SweeperEnabled", true))
        {
            services.AddHostedService<ExpiredReservationSweeperService>();
        }

        services.AddScoped<PessimisticSeatAllocationStrategy>();
        services.AddScoped<OptimisticSeatAllocationStrategy>();
        services.AddScoped<ISeatAllocationStrategy>(provider =>
        {
            var strategy = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ReservationSettings>>()
                .Value
                .ConcurrencyStrategy;

            return string.Equals(strategy, "Optimistic", StringComparison.OrdinalIgnoreCase)
                ? provider.GetRequiredService<OptimisticSeatAllocationStrategy>()
                : provider.GetRequiredService<PessimisticSeatAllocationStrategy>();
        });

        services.AddOptions<ReservationSettings>()
            .Bind(configuration.GetSection(ReservationSettings.SectionName))
            .Validate(
                settings => settings.HoldDuration > TimeSpan.Zero
                    && settings.HoldDuration <= TimeSpan.FromHours(24),
                "Reservations:HoldDuration must be between zero and 24 hours.")
            .Validate(
                settings => settings.AvailabilityCacheTtl >= TimeSpan.Zero,
                "Reservations:AvailabilityCacheTtl cannot be negative.")
            .Validate(
                settings => settings.ConcurrencyStrategy is "Pessimistic" or "Optimistic",
                "Reservations:ConcurrencyStrategy must be 'Pessimistic' or 'Optimistic'.")
            .Validate(
                settings => settings.SweeperInterval > TimeSpan.Zero
                    && settings.SweeperInterval <= TimeSpan.FromHours(1),
                "Reservations:SweeperInterval must be between zero and one hour.")
            .Validate(
                settings => settings.SweeperBatchSize is > 0 and <= 1000,
                "Reservations:SweeperBatchSize must be between 1 and 1000.")
            .ValidateOnStart();

        return services;
    }
}
