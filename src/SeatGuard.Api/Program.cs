using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SeatGuard.Api.Authentication;
using SeatGuard.Api.Endpoints;
using SeatGuard.Api.Errors;
using SeatGuard.Api.Middleware;
using SeatGuard.Application;
using SeatGuard.Infrastructure;
using SeatGuard.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(
        ApiKeyAuthenticationHandler.PolicyName,
        policy => policy.RequireAuthenticatedUser());

var bookingsPerMinute = builder.Configuration.GetValue("RateLimiting:BookingsPerMinute", 120);
var adminPerMinute = builder.Configuration.GetValue("RateLimiting:AdminPerMinute", 30);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("bookings", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = bookingsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    options.AddPolicy("admin", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = adminPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SeatGuardDbContext>("database", tags: ["ready"]);

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("SeatGuard API"));
}

app.MapGet("/", () => Results.Ok(new
{
    service = "SeatGuard",
    status = "ok",
    documentation = "/scalar"
}));

app.MapEventEndpoints();
app.MapReservationEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();

public partial class Program;
