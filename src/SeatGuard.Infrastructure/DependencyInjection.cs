using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Infrastructure.Persistence;
using SeatGuard.Infrastructure.Persistence.Repositories;

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

        return services;
    }
}
