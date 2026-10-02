using System.Net;

namespace SuperApp.Gateway.Proxy.Resilience;

/// <summary>
/// Delegating handler that retries proxied requests to a domain service, but only idempotent requests without a body (GET, HEAD), and only
/// after transient failures: a connection error (<see cref="HttpRequestException"/>) or status 502, 503 or 504 (ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// POST, PUT, PATCH, DELETE and any request with a body are sent exactly once: repeating them could execute a command twice.
/// Other status codes (including 500 and 4xx) are returned to the client unchanged. Timeouts and cancellations are not retried.
/// </para>
/// <para>
/// A request is sent at most <c>1 + maxRetries</c> times, with exponential backoff between attempts: <c>baseDelay × 2^attempt</c>
/// (with the defaults 100 ms, then 200 ms). After the last attempt the last response is returned, or the last connection error is thrown.
/// Each retry is logged (events 3401, 3402). Created per cluster by <see cref="SuperApp.Gateway.Proxy.Resilience.IdempotentRetryForwarderHttpClientFactory"/>.
/// </para>
/// </remarks>
/// <param name="maxRetries">Maximum number of retries after the first attempt (<c>Gateway:Retry:MaxRetries</c>, default 2); 0 disables retries.</param>
/// <param name="baseDelay">Delay before the first retry; doubled for every further retry (<c>Gateway:Retry:BaseDelay</c>, default 100 ms).</param>
/// <param name="logger">Logger for retries.</param>
internal sealed partial class IdempotentRetryHandler(int maxRetries, TimeSpan baseDelay, ILogger logger) : DelegatingHandler
{
    /// <summary>Sends the request, retrying it as described on <see cref="SuperApp.Gateway.Proxy.Resilience.IdempotentRetryHandler"/> when it is retryable.</summary>
    /// <param name="request">The request to the domain service.</param>
    /// <param name="cancellationToken">Cancellation of the proxied request; stops retrying.</param>
    /// <returns>The first non-transient response, or the last response when all retries are used up.</returns>
    /// <exception cref="HttpRequestException">The connection failed on the last allowed attempt, or on the only attempt of a non-retryable request.</exception>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsRetryable(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                if (attempt >= maxRetries || !IsTransient(response.StatusCode))
                {
                    return response;
                }

                LogRetrying(logger, request.Method.Method, request.RequestUri, (int)response.StatusCode, attempt + 1);
                response.Dispose();
            }
            catch (HttpRequestException exception) when (attempt < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                LogRetryingAfterError(logger, request.Method.Method, request.RequestUri, attempt + 1, exception);
            }

            await Task.Delay(baseDelay * Math.Pow(2, attempt), cancellationToken);
        }
    }

    private static bool IsRetryable(HttpRequestMessage request) =>
        (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head) && request.Content is null;

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    [LoggerMessage(3401, LogLevel.Warning, "Retrying {Method} {Uri} after status {StatusCode} (attempt {Attempt})")]
    private static partial void LogRetrying(ILogger logger, string method, Uri? uri, int statusCode, int attempt);

    [LoggerMessage(3402, LogLevel.Warning, "Retrying {Method} {Uri} after connection error (attempt {Attempt})")]
    private static partial void LogRetryingAfterError(ILogger logger, string method, Uri? uri, int attempt, Exception exception);
}
