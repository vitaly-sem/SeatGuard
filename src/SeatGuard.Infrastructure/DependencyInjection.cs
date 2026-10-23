using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SeatGuard.Application.Abstractions.Caching;
using SeatGuard.Application.Abstractions.Messaging;
using SeatGuard.Application.Abstractions.Payments;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Abstractions.Services;
using SeatGuard.Application.Common;
using SeatGuard.Infrastructure.Caching;
using SeatGuard.Infrastructure.Concurrency;
using SeatGuard.Infrastructure.Outbox;
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

        // Npgsql probes for the Kerberos library when GSS encryption is left at
        // its default ("Prefer"). The chiseled runtime does not ship that library
        // and password authentication does not need it. Operators can still opt
        // in explicitly with "Gss Encryption Mode=Require".
        if (!connectionString.Contains("gss encryption mode", StringComparison.OrdinalIgnoreCase))
        {
            connectionString += ";Gss Encryption Mode=Disable";
        }

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

        services.AddSingleton<IIntegrationEventPublisher, LoggingIntegrationEventPublisher>();
        services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();
        if (configuration.GetValue("Outbox:Enabled", true))
        {
            services.AddHostedService<OutboxDispatcherService>();
        }

        services.AddScoped<IOutboxCleaner, OutboxCleaner>();
        if (configuration.GetValue("Outbox:CleanupEnabled", true))
        {
            services.AddHostedService<OutboxCleanupService>();
        }

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

        services.AddOptions<OutboxSettings>()
            .Bind(configuration.GetSection(OutboxSettings.SectionName))
            .Validate(settings => settings.PollInterval > TimeSpan.Zero, "Outbox:PollInterval must be positive.")
            .Validate(settings => settings.BatchSize is > 0 and <= 1000, "Outbox:BatchSize must be between 1 and 1000.")
            .Validate(settings => settings.MaxAttempts > 0, "Outbox:MaxAttempts must be positive.")
            .Validate(settings => settings.LeaseDuration > TimeSpan.Zero, "Outbox:LeaseDuration must be positive.")
            .Validate(
                settings => settings.RetentionDays is >= 0 and <= 3650,
                "Outbox:RetentionDays must be between 0 and 3650.")
            .Validate(
                settings => settings.CleanupInterval > TimeSpan.Zero,
                "Outbox:CleanupInterval must be positive.")
            .Validate(
                settings => settings.CleanupBatchSize is > 0 and <= 10000,
                "Outbox:CleanupBatchSize must be between 1 and 10000.")
            .ValidateOnStart();

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
