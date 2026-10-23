using MediatR;
using Microsoft.Extensions.Logging;

namespace SeatGuard.Application.Common.Behaviors;

/// <summary>
/// Logs command and query execution without logging payloads, which may contain
/// customer identifiers. The elapsed time is the first place to look when a
/// handler slows down.
/// </summary>
internal sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("Handling {RequestName}", requestName);

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            var response = await next(cancellationToken);

            logger.LogInformation(
                "Handled {RequestName} in {ElapsedMilliseconds} ms",
                requestName,
                System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            return response;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Handler {RequestName} failed after {ElapsedMilliseconds} ms",
                requestName,
                System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            throw;
        }
    }
}
