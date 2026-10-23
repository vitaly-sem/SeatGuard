using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using SeatGuard.Api.Authentication;
using SeatGuard.Api.Endpoints;
using SeatGuard.Api.Errors;
using SeatGuard.Api.Health;
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
var readsPerMinute = builder.Configuration.GetValue("RateLimiting:ReadsPerMinute", 300);
var forwardedHeadersEnabled = builder.Configuration.GetValue("ForwardedHeaders:Enabled", false);

if (forwardedHeadersEnabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 2;

        foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("bookings", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ClientPartitionKey(context),
        factory: _ => FixedWindow(bookingsPerMinute)));

    // Run before authentication and partition by the presented key: a flood of
    // bad keys is throttled without exhausting the valid administrator's bucket.
    // The raw key is hashed so limiter diagnostics never contain a credential.
    options.AddPolicy("admin", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: AdminPartitionKey(context),
        factory: _ => FixedWindow(adminPerMinute)));

    options.AddPolicy("reads", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ClientPartitionKey(context),
        factory: _ => FixedWindow(readsPerMinute)));

    static FixedWindowRateLimiterOptions FixedWindow(int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    };

    static string ClientPartitionKey(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    static string AdminPartitionKey(HttpContext context)
    {
        var key = context.Request.Headers[ApiKeyAuthenticationHandler.HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(key))
        {
            return $"anonymous:{ClientPartitionKey(context)}";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        return $"key:{Convert.ToHexString(hash)}";
    }
});

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SeatGuardDbContext>("database", tags: ["ready"])
    .AddCheck<DatabaseMigrationHealthCheck>("migrations", tags: ["ready"]);

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

// The client address must be resolved from trusted proxy headers before the
// rate limiter partitions requests, and limiting must run before authentication
// so failed admin keys consume a budget instead of short-circuiting at 401.
if (forwardedHeadersEnabled)
{
    app.UseForwardedHeaders();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var documentationExposed = app.Environment.IsDevelopment()
    || app.Configuration.GetValue<bool>("Api:ExposeDocumentation");

if (documentationExposed)
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("SeatGuard API"));
}

app.MapGet("/", () => Results.Redirect("/ui/"));

app.MapGet("/api/v1/meta", () => Results.Ok(new { documentation = documentationExposed }))
    .WithName("GetApiMetadata")
    .WithSummary("Describes optional capabilities the console can link to")
    .RequireRateLimiting("reads")
    .Produces(StatusCodes.Status200OK);

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
