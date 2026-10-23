using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Respawn;
using Respawn.Graph;
using SeatGuard.Application.Common;
using SeatGuard.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SeatGuard.IntegrationTests.Infrastructure;

/// <summary>
/// Owns one PostgreSQL container for the whole test run and creates a fresh
/// application host per test. The database is truncated before every host so
/// tests never depend on each other's residue.
/// </summary>
public sealed class SeatGuardFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("seatguard")
        .WithUsername("seatguard")
        .WithPassword("seatguard")
        .Build();

    private Respawner? _respawner;

    /// <summary>
    /// Frozen clock shared by every host. Tests advance it explicitly to drive
    /// hold expiry; nothing else moves it.
    /// </summary>
    public FakeTimeProvider Clock { get; } =
        new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

    private static readonly string[] EnvironmentKeys =
    [
        "ConnectionStrings__SeatGuard",
        "Admin__ApiKey",
        "Database__MigrateOnStartup",
        "Reservations__SweeperEnabled",
        "Outbox__Enabled",
        "Reservations__HoldDuration",
        "RateLimiting__BookingsPerMinute",
        "RateLimiting__AdminPerMinute",
        "Observability__Enabled",
    ];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Program reads several settings while registering services, before a
        // WebHostBuilder configuration callback would run. Environment variables
        // are part of builder.Configuration from the start, so they are the only
        // reliable seam for those early reads.
        Environment.SetEnvironmentVariable("ConnectionStrings__SeatGuard", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Admin__ApiKey", TestApi.AdminKey);
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "true");
        Environment.SetEnvironmentVariable("Reservations__SweeperEnabled", "false");
        Environment.SetEnvironmentVariable("Outbox__Enabled", "false");
        Environment.SetEnvironmentVariable("Reservations__HoldDuration", "00:05:00");
        Environment.SetEnvironmentVariable("RateLimiting__BookingsPerMinute", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__AdminPerMinute", "10000");
        Environment.SetEnvironmentVariable("Observability__Enabled", "false");

        // The first host applies migrations, which lets Respawn discover tables.
        await using (var bootstrap = await StartAppAsync())
        {
            _ = bootstrap.Client;
        }

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Table("public", "__EFMigrationsHistory")],
            WithReseed = true,
        });
    }

    public async Task<SeatGuardApp> StartAppAsync(
        string concurrencyStrategy = "Pessimistic",
        Action<IServiceCollection>? configureServices = null)
    {
        if (_respawner is not null)
        {
            await ResetAsync();
        }

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);

                services.Configure<ReservationSettings>(settings =>
                {
                    settings.ConcurrencyStrategy = concurrencyStrategy;
                    settings.HoldDuration = TimeSpan.FromMinutes(5);
                    settings.SweeperEnabled = false;
                });

                services.Configure<OutboxSettings>(settings => settings.Enabled = false);

                configureServices?.Invoke(services);
            });
        });

        var app = new SeatGuardApp(factory);

        await using (var scope = app.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();
            var actual = dbContext.Database.GetDbConnection().ConnectionString;

            if (!string.Equals(actual, _postgres.GetConnectionString(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The test host is not using the Testcontainers database. " +
                    $"Expected '{_postgres.GetConnectionString()}', got '{actual}'.");
            }

            await dbContext.Database.MigrateAsync();
        }

        return app;
    }

    public async Task ResetAsync()
    {
        if (_respawner is null)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async Task DisposeAsync()
    {
        foreach (var key in EnvironmentKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        await _postgres.DisposeAsync();
    }
}

public sealed class SeatGuardApp(WebApplicationFactory<Program> factory) : IAsyncDisposable
{
    public WebApplicationFactory<Program> Factory { get; } = factory;

    public HttpClient Client { get; } = factory.CreateClient();

    public HttpClient CreateClient(bool allowAutoRedirect)
        => Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
        });

    public AsyncServiceScope CreateScope() => Factory.Services.CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationSuite : ICollectionFixture<SeatGuardFixture>
{
    public const string Name = "integration";
}
