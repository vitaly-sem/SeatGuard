using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SeatGuard.Infrastructure.Persistence;

/// <summary>
/// Used by `dotnet ef` at design time. The runtime application registers the
/// DbContext through <c>AddInfrastructure</c> instead.
/// </summary>
public sealed class SeatGuardDbContextFactory : IDesignTimeDbContextFactory<SeatGuardDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=55432;Database=seatguard;Username=seatguard;Password=seatguard";

    public SeatGuardDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SEATGUARD_CONNECTION") ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<SeatGuardDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SeatGuardDbContext(options, TimeProvider.System);
    }
}
