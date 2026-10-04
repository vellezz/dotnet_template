using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;

namespace SuperApp.Gateway.Security;

/// <summary>
/// Rate limiting policies of the gateway, defined in code and referenced by name from
/// <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRoute.RateLimiterPolicy"/> (ADR-0006, ADR-0022).
/// </summary>
/// <remarks>
/// Rate limit counters are distributed across replicas using Redis when available (<see cref="RedisFixedWindowRateLimiter"/>),
/// ensuring consistent enforcement regardless of pod replica count. If Redis is unavailable, the policy automatically falls back to in-memory counters.
/// A route that names a policy not registered here is rejected by the YARP validator when the configuration is loaded.
/// </remarks>
public static class GatewayRateLimits
{
    /// <summary>
    /// Name of the fixed-window (1 minute) policy partitioned by the <c>sub</c> claim, or by the client IP address for anonymous requests.
    /// Routes reference it in <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRoute.RateLimiterPolicy"/>.
    /// </summary>
    public const string PerUser = "per-user";

    /// <summary>
    /// Registers the <see cref="PerUser"/> policy. The permit limit and window are read from configuration
    /// (<c>Gateway:RateLimit:PermitPerMinute</c>, default 600; <c>Gateway:RateLimit:Window</c>, default 1 minute;
    /// <c>Gateway:RateLimit:KeyPrefix</c>, default <c>gateway:rl</c>); once it is exceeded, requests get status 429 immediately,
    /// without queuing, with the same problem body as every other error (<c>code</c> <c>http.too_many_requests</c>, <c>traceId</c>; ADR-0044).
    /// </summary>
    /// <param name="options">Options of the rate limiting middleware the policy is added to.</param>
    /// <param name="configuration">Application configuration the limit and window are read from.</param>
    public static void Register(RateLimiterOptions options, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("Gateway:RateLimit:PermitPerMinute", 600);
        var window = configuration.GetValue("Gateway:RateLimit:Window", TimeSpan.FromMinutes(1));
        var keyPrefix = configuration.GetValue("Gateway:RateLimit:KeyPrefix", "gateway:rl");

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, cancellationToken) => WriteRejectionAsync(context.HttpContext);
        options.AddPolicy(PerUser, context =>
        {
            var redis = context.RequestServices.GetService<IConnectionMultiplexer>();
            var partitionKey = context.User.FindFirst("sub")?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown";

            return RateLimitPartition.Get(
                partitionKey,
                key => new RedisFixedWindowRateLimiter(redis, $"{keyPrefix}:{PerUser}", key, permitLimit, window));
        });
    }

    // Writes the problem through IProblemDetailsService, so ProblemDetailsConventions adds code and traceId like for every other error.
    private static async ValueTask WriteRejectionAsync(HttpContext httpContext)
    {
        var problems = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests." },
        });
    }
}
