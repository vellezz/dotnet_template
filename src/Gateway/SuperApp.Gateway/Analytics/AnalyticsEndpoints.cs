using SuperApp.Framework.Infrastructure.Analytics;
using SuperApp.Gateway.Hosting;
using SuperApp.Gateway.Security;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace SuperApp.Gateway.Analytics;

/// <summary>
/// Gateway endpoints of product analytics (ADR-0036): the PostHog proxy <c>/ingest</c> of the <c>bff-web</c> profile and the analytics
/// identifier endpoint of the <c>gateway-mobile</c> profile.
/// </summary>
/// <remarks>
/// <list type="table">
///   <listheader><term>Endpoint</term><description>Behavior</description></listheader>
///   <item>
///     <term><c>/ingest/{**path}</c> (bff-web)</term>
///     <description>
///     Anonymous, rate limited per user (or per address before login). Forwards the browser library's requests to PostHog Cloud EU
///     (<see cref="AnalyticsOptions.Host"/>); <c>/ingest/static/{**path}</c> goes to <see cref="AnalyticsOptions.AssetsHost"/>. The prefix
///     <c>/ingest</c> is removed. The web client is configured with <c>api_host: '/ingest'</c>, so analytics is same-origin: it is not
///     blocked by browser content blockers and needs no external domain in the content security policy. Mapped only when analytics is
///     enabled; otherwise <c>/ingest</c> is not mapped (the gateway answers it like any unknown path: 401 under the fallback
///     authorization policy) and the SPA must not initialize PostHog (<c>analyticsId</c> in
///     <c>/bff/user</c> is <see langword="null"/>).
///     </description>
///   </item>
///   <item>
///     <term><c>GET /analytics/id</c> (gateway-mobile)</term>
///     <description>
///     Requires a valid access token; returns <c>{ "analyticsId": "u_..." }</c>, the pseudonymous identifier the mobile app passes to
///     <c>PostHog.identify</c>, or <c>{ "analyticsId": null }</c> when analytics is disabled. The web client gets the same value from
///     <c>/bff/user</c>.
///     </description>
///   </item>
/// </list>
/// <para>
/// The proxy is defined in code, not in the database route configuration (ADR-0022): database routes may point only at Services inside the
/// cluster and always receive the user's access token, while this destination is external and must never receive it. Before forwarding,
/// the proxy removes <c>Cookie</c> (the BFF session), <c>Authorization</c> and every <c>X-User-*</c> header, and does not add
/// <c>X-Forwarded-*</c> headers, so PostHog does not learn the user's IP address from the gateway (ADR-0036).
/// </para>
/// </remarks>
internal static class AnalyticsEndpoints
{
    /// <summary>Path prefix of the PostHog proxy in the <c>bff-web</c> profile (<c>/ingest</c>).</summary>
    public const string IngestPrefix = "/ingest";

    /// <summary>Maps the analytics endpoints of <paramref name="profile"/> described on <see cref="AnalyticsEndpoints"/>.</summary>
    /// <param name="app">The gateway application.</param>
    /// <param name="profile">The gateway profile (<see cref="GatewayProfiles"/>).</param>
    public static void MapAnalyticsEndpoints(this WebApplication app, string profile)
    {
        var options = app.Services.GetRequiredService<IOptions<AnalyticsOptions>>().Value;

        if (profile == GatewayProfiles.Mobile)
        {
            app.MapGet("/analytics/id", (HttpContext context, AnalyticsIdentity identity) =>
                    Results.Ok(new { analyticsId = identity.ForSubject(context.User.FindFirst("sub")?.Value) }))
                .RequireAuthorization()
                .RequireRateLimiting(GatewayRateLimits.PerUser);
            return;
        }

        if (!options.Enabled)
        {
            return;
        }

        app.MapForwarder($"{IngestPrefix}/static/{{**path}}", options.AssetsHost.ToString(), ConfigureTransforms)
            .AllowAnonymous()
            .RequireRateLimiting(GatewayRateLimits.PerUser);
        app.MapForwarder($"{IngestPrefix}/{{**path}}", options.Host.ToString(), ConfigureTransforms)
            .AllowAnonymous()
            .RequireRateLimiting(GatewayRateLimits.PerUser);
    }

    // The request reaches PostHog with the browser library's own headers and body only: no session cookie, no token, no identity headers,
    // no client IP from the gateway. The Host header is the destination's (YARP's default).
    private static void ConfigureTransforms(TransformBuilderContext context)
    {
        context.UseDefaultForwarders = false;
        context.AddPathRemovePrefix(IngestPrefix);
        context.AddRequestHeaderRemove("Cookie");
        context.AddRequestHeaderRemove("Authorization");
        context.AddRequestTransform(transform =>
        {
            var removed = transform.ProxyRequest.Headers
                .Select(header => header.Key)
                .Where(name => name.StartsWith("X-User-", StringComparison.OrdinalIgnoreCase)
                               || name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
                               || name.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)
                               || name.Equals("X-CSRF", StringComparison.OrdinalIgnoreCase))
                .ToList();
            removed.ForEach(name => transform.ProxyRequest.Headers.Remove(name));
            return ValueTask.CompletedTask;
        });
        context.AddResponseHeaderRemove("Set-Cookie");
    }
}
