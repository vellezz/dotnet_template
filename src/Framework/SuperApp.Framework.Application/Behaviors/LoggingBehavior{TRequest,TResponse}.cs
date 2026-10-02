using SuperApp.Framework.Application.Telemetry;
using SuperApp.Framework.Domain.Results;
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SuperApp.Framework.Application.Behaviors;

/// <summary>
/// First (outermost) pipeline behavior: measures every command and query, creates a tracing span for it and logs the outcome (ADR-0017).
/// </summary>
/// <remarks>
/// Successful requests are logged at Information level with the elapsed time; failed results at Warning level with the error code.
/// The request payload is never logged, because it may contain personal data. Exceptions are not caught here; they propagate to the host.
/// </remarks>
internal sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResultFactory<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        using var activity = ApplicationTelemetry.ActivitySource.StartActivity(requestName);
        var started = Stopwatch.GetTimestamp();

        var response = await next();

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (response is Result { IsFailure: true } failure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, failure.Error.Code);
            LogRequestFailed(logger, requestName, failure.Error.Code, elapsed);
        }
        else
        {
            LogRequestHandled(logger, requestName, elapsed);
        }

        return response;
    }

    [LoggerMessage(100, LogLevel.Information, "Request {RequestName} handled in {ElapsedMs} ms")]
    private static partial void LogRequestHandled(ILogger logger, string requestName, double elapsedMs);

    [LoggerMessage(101, LogLevel.Warning, "Request {RequestName} failed with {ErrorCode} in {ElapsedMs} ms")]
    private static partial void LogRequestFailed(ILogger logger, string requestName, string errorCode, double elapsedMs);
}
