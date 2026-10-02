using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace SuperApp.Framework.Infrastructure.Api;

/// <summary>
/// Turns a failed call to another API of the system (a domain service, another experience's BFF) into a problem response instead of a 500:
/// <c>503 downstream.unavailable</c> or <c>504 downstream.timeout</c>. The exception may be wrapped (for example by Refit); its inner
/// exceptions are inspected.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <c>AddDownstreamApi</c> and used by <c>UseExceptionHandler</c>. It handles only failures of the transport, after the
/// resilience pipeline has given up: <see cref="HttpRequestException"/> (connection refused, DNS, reset), <see cref="BrokenCircuitException"/>
/// (circuit open after repeated failures) and <see cref="TimeoutRejectedException"/> (timeout). An answer of the callee, including an error
/// status, is not an exception (Refit <c>IApiResponse</c>) and is relayed by <see cref="DownstreamResponseExtensions"/>.
/// </para>
/// <para>
/// Cancellation of the incoming request by the client is not handled here: the response is no longer read. Other exceptions are left to the
/// default handler (500), because they indicate a bug.
/// </para>
/// </remarks>
/// <param name="logger">Logger for the failures (event ID 220).</param>
internal sealed partial class DownstreamUnavailableExceptionHandler(ILogger<DownstreamUnavailableExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var failure = Unwrap(exception);
        var (status, code, title) = failure switch
        {
            TimeoutRejectedException => (StatusCodes.Status504GatewayTimeout, "downstream.timeout", "Usługa zależna nie odpowiedziała w czasie."),
            BrokenCircuitException or HttpRequestException => (StatusCodes.Status503ServiceUnavailable, "downstream.unavailable", "Usługa zależna jest niedostępna."),
            _ => (0, string.Empty, string.Empty),
        };

        if (status == 0 || httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        LogDownstreamFailed(logger, code, exception);
        var problem = new ProblemDetails { Status = status, Title = title, Instance = httpContext.Request.Path };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = ProblemDetailsConventions.TraceId(httpContext);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
        return true;
    }

    // Refit and the resilience pipeline may wrap the transport failure (e.g. in an ApiException); the innermost known failure decides.
    private static Exception? Unwrap(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutRejectedException or BrokenCircuitException or HttpRequestException)
            {
                return current;
            }
        }

        return null;
    }

    [LoggerMessage(220, LogLevel.Warning, "Downstream call failed ({Code})")]
    private static partial void LogDownstreamFailed(ILogger logger, string code, Exception exception);
}
