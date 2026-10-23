using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using SeatGuard.Api.Authentication;
using SeatGuard.Api.Endpoints;
using SeatGuard.Api.Errors;
using SeatGuard.Api.Middleware;
using SeatGuard.Application;
using SeatGuard.Application.Observability;
using SeatGuard.Infrastructure;
using SeatGuard.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

var serviceName = builder.Configuration["Observability:ServiceName"] ?? "seatguard-api";
var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"];
var observabilityEnabled = builder.Configuration.GetValue("Observability:Enabled", true);

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Host.UseSerilog((context, _, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithMachineName()
        .Enrich.WithProperty("service", serviceName)
        .WriteTo.Console(new CompactJsonFormatter()));
}

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

if (observabilityEnabled)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(
                serviceName,
                serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0")
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = builder.Environment.EnvironmentName,
            }))
        .WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health")
                        && !context.Request.Path.StartsWithSegments("/metrics");
                })
                .AddHttpClientInstrumentation()
                .AddSource("Npgsql")
                .AddSource("Microsoft.EntityFrameworkCore");

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
            }
        })
        .WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(SeatGuardTelemetry.MeterName)
                .AddPrometheusExporter();

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
            }
        });
}

var app = builder.Build();

if (args.Contains("--migrate"))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var migrationContext = migrationScope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();
    await migrationContext.Database.MigrateAsync();
    app.Logger.LogInformation("Database migrations applied");
    return;
}

if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseMiddleware<CorrelationIdMiddleware>();

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = (httpContext, _, exception) =>
        {
            if (exception is not null)
            {
                return LogEventLevel.Error;
            }

            var path = httpContext.Request.Path;

            return path.StartsWithSegments("/health")
                || path.StartsWithSegments("/metrics")
                || path.StartsWithSegments("/ui")
                || path.StartsWithSegments("/favicon")
                ? LogEventLevel.Debug
                : LogEventLevel.Information;
        };
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("CorrelationId", httpContext.TraceIdentifier);
            diagnosticContext.Set("TraceId", System.Diagnostics.Activity.Current?.TraceId.ToString());
        };
    });
}

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Api:ExposeDocumentation"))
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("SeatGuard API"));
}

app.MapGet("/", () => Results.Redirect("/ui/"));

app.MapEventEndpoints();
app.MapReservationEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthReportAsync
});

if (observabilityEnabled)
{
    app.MapPrometheusScrapingEndpoint("/metrics");
}

app.Run();

static Task WriteHealthReportAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    return context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
            description = entry.Value.Description,
        }),
    });
}

public partial class Program;
