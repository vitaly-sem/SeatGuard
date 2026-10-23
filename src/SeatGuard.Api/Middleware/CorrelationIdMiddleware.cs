using System.Diagnostics;

namespace SeatGuard.Api.Middleware;

/// <summary>
/// Accepts an inbound correlation id or creates one, echoes it back and exposes
/// it to logging and tracing. Using the trace id keeps logs and spans joinable.
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        await next(context);
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            var provided = values.ToString();

            if (!string.IsNullOrWhiteSpace(provided) && provided.Length <= 128)
            {
                return provided;
            }
        }

        return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    }
}
