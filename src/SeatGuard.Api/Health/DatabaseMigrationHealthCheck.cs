using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SeatGuard.Infrastructure.Persistence;

namespace SeatGuard.Api.Health;

/// <summary>
/// Readiness must fail while migrations are pending. Otherwise a new pod can
/// become Ready against a schema that the migration Job has not created yet.
/// </summary>
internal sealed class DatabaseMigrationHealthCheck(SeatGuardDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

        return pending.Length == 0
            ? HealthCheckResult.Healthy("Schema is up to date.")
            : HealthCheckResult.Unhealthy(
                $"{pending.Length} pending migration(s): {string.Join(", ", pending)}");
    }
}
