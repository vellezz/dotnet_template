using SuperApp.Gateway.Hosting;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace SuperApp.Gateway.Proxy.Transforms;

/// <summary>
/// Security transforms applied by YARP to every route of both profiles. They live in code and cannot be configured or disabled from the
/// database (ADR-0006, ADR-0022).
/// </summary>
/// <remarks>
/// <para>For every proxied request:</para>
/// <list type="bullet">
///   <item><description>the <c>Cookie</c> header is removed, so the BFF session cookie never reaches a domain service;</description></item>
///   <item><description>every <c>X-User-*</c> header is removed, so a client cannot forge identity headers. Services must take identity
///   only from the validated token (ADR-0007).</description></item>
///   <item><description>in the <c>bff-web</c> profile, any <c>Authorization</c> header sent by the browser is dropped and replaced with
///   <c>Authorization: Bearer &lt;access token&gt;</c> taken from the server-side session (<see cref="SuperApp.Gateway.Bff.Sessions.DbTicketStore"/>, refreshed by
///   <see cref="SuperApp.Gateway.Bff.Tokens.TokenRefresher"/>). Without a session no <c>Authorization</c> header is sent.</description></item>
/// </list>
/// <para>In the <c>gateway-mobile</c> profile the client's own <c>Authorization</c> header is forwarded unchanged (ADR-0012).</para>
/// </remarks>
internal static class SecurityTransforms
{
    /// <summary>Adds the security transforms described on <see cref="SuperApp.Gateway.Proxy.Transforms.SecurityTransforms"/> to a route; called by YARP for each route.</summary>
    /// <param name="context">YARP transform builder context of the route.</param>
    /// <param name="profile">The gateway profile (<see cref="SuperApp.Gateway.Hosting.GatewayProfiles"/>); decides whether the session token is attached.</param>
    public static void Apply(TransformBuilderContext context, string profile)
    {
        context.AddRequestHeaderRemove("Cookie");
        context.AddRequestTransform(transform =>
        {
            var identityHeaders = transform.ProxyRequest.Headers
                .Select(header => header.Key)
                .Where(name => name.StartsWith("X-User-", StringComparison.OrdinalIgnoreCase))
                .ToList();
            identityHeaders.ForEach(name => transform.ProxyRequest.Headers.Remove(name));
            return ValueTask.CompletedTask;
        });

        if (profile == GatewayProfiles.BffWeb)
        {
            // The browser holds no tokens; the BFF attaches the access token from the server-side session.
            context.AddRequestTransform(async transform =>
            {
                transform.ProxyRequest.Headers.Authorization = null;
                var accessToken = await transform.HttpContext.GetTokenAsync("access_token");
                if (accessToken is not null)
                {
                    transform.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                }
            });
        }
    }
}
