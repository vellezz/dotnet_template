using SuperApp.Framework.Infrastructure.Analytics;
using SuperApp.Gateway.Bff.Sessions;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SuperApp.Gateway.Bff;

/// <summary>
/// The <c>/bff/*</c> endpoints the Angular SPA and the CIAM talk to in the <c>bff-web</c> profile (ADR-0006, ADR-0011). Mapped only
/// in that profile.
/// </summary>
/// <remarks>
/// <list type="table">
///   <listheader><term>Endpoint</term><description>Behavior</description></listheader>
///   <item>
///     <term><c>GET /bff/login?returnUrl=/path</c></term>
///     <description>Anonymous. Starts the OIDC Authorization Code + PKCE flow (302 to the CIAM login page). After a successful login the
///     OIDC handler creates the session (stored by <see cref="SuperApp.Gateway.Bff.Sessions.DbTicketStore"/>), sets the <c>__Host-bff</c> cookie and redirects to
///     <c>returnUrl</c>. Only local paths are accepted (<c>/…</c>, not <c>//…</c> or <c>/\…</c>); anything else redirects to <c>/</c>,
///     which prevents open redirects. The SPA navigates here with a full page load, never with XHR.</description>
///   </item>
///   <item>
///     <term><c>GET /bff/logout?sid={sid}</c></term>
///     <description>Top-level navigation of the browser (<c>window.location.href = logoutUrl</c>), never XHR/fetch: only a navigation can
///     follow the cross-origin 302 to the CIAM end-session endpoint. The <c>sid</c> query parameter must equal the <c>sid</c> claim of the
///     session (the SPA takes the ready-made <c>logoutUrl</c> from <c>/bff/user</c>); this is the CSRF protection of the endpoint, because
///     another site cannot know the value. A missing or different <c>sid</c> is answered with 400 and the session stays. With a matching
///     <c>sid</c> it signs out of the cookie scheme (the session row is deleted) and of the OIDC scheme (redirect to the CIAM end-session
///     endpoint, then back to <c>/</c>). Without a session there is nothing to end, so it redirects to <c>/</c>. Not covered by the
///     <c>X-CSRF</c> header check (<see cref="SuperApp.Gateway.Bff.Security.CsrfHeaderMiddleware"/>), because a navigation cannot send custom headers.</description>
///   </item>
///   <item>
///     <term><c>GET /bff/user</c></term>
///     <description>Requires a session; returns <c>{ sub, name, scopes, logoutUrl, analyticsId }</c> of the logged-in user, or 401 without a
///     session. The SPA calls it at startup to find out whether the user is logged in. <c>logoutUrl</c> is <c>/bff/logout?sid=...</c> for the
///     current session, or <see langword="null"/> when the CIAM did not issue a <c>sid</c> claim (then the BFF logout is not possible).
///     <c>analyticsId</c> is the pseudonymous PostHog identifier (<see cref="AnalyticsIdentity"/>) the SPA passes to <c>posthog.identify</c>
///     after the user's consent, or <see langword="null"/> when analytics is disabled (ADR-0036). Tokens are never returned.</description>
///   </item>
///   <item>
///     <term><c>POST /bff/backchannel-logout</c></term>
///     <description>Called server-to-server by the CIAM when the user's SSO session ends (OIDC Back-Channel Logout). Anonymous and exempt
///     from antiforgery and from the CSRF header check; trust comes from validating the signed <c>logout_token</c>. Deletes the matching
///     sessions from <c>gateway.Sessions</c>, so the next request with the old cookie gets 401.</description>
///   </item>
/// </list>
/// </remarks>
internal static partial class BffEndpoints
{
    private const string BackchannelLogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

    /// <summary>Maps the <c>/bff</c> endpoint group described on <see cref="SuperApp.Gateway.Bff.BffEndpoints"/>.</summary>
    /// <param name="app">The gateway application; must be configured with the cookie and OpenID Connect schemes of the <c>bff-web</c> profile.</param>
    public static void MapBffEndpoints(this WebApplication app)
    {
        var bff = app.MapGroup("/bff");

        bff.MapGet("/login", (string? returnUrl, HttpContext context) =>
            Results.Challenge(
                new AuthenticationProperties { RedirectUri = IsLocalUrl(returnUrl) ? returnUrl : "/" },
                [OpenIdConnectDefaults.AuthenticationScheme]))
            .AllowAnonymous();

        bff.MapGet("/logout", Logout)
            .AllowAnonymous();

        bff.MapGet("/user", (HttpContext context, AnalyticsIdentity analytics) => Results.Ok(new
        {
            sub = context.User.FindFirst("sub")?.Value,
            name = context.User.FindFirst("name")?.Value,
            scopes = context.User.FindAll("scope").SelectMany(claim => claim.Value.Split(' ')).Distinct().ToArray(),
            logoutUrl = LogoutUrl(context.User),
            analyticsId = analytics.ForSubject(context.User.FindFirst("sub")?.Value),
        }))
            .RequireAuthorization();

        bff.MapPost("/backchannel-logout", HandleBackchannelLogoutAsync)
            .AllowAnonymous()
            .DisableAntiforgery();
    }

    // Validates the logout token as required by OIDC Back-Channel Logout 1.0: signature (CIAM signing keys from the discovery document),
    // issuer, audience = our client id, lifetime, no "nonce" claim and the back-channel logout event in "events". Then revokes sessions
    // by "sid" (one SSO session) or, when the CIAM sends only "sub", all sessions of that user. Responds 400 for anything invalid, 200 otherwise.
    private static async Task<IResult> HandleBackchannelLogoutAsync(
        HttpContext context,
        IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
        DbTicketStore ticketStore,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(BffEndpoints));
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var logoutToken = form["logout_token"].ToString();
        if (string.IsNullOrEmpty(logoutToken))
        {
            return Results.BadRequest();
        }

        var options = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var configuration = await options.ConfigurationManager!.GetConfigurationAsync(context.RequestAborted);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(logoutToken, new TokenValidationParameters
        {
            ValidIssuer = configuration.Issuer,
            ValidAudience = options.ClientId,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateLifetime = true,
        });

        if (!validation.IsValid
            || validation.ClaimsIdentity.FindFirst("nonce") is not null
            || validation.ClaimsIdentity.FindFirst("events")?.Value.Contains(BackchannelLogoutEvent, StringComparison.Ordinal) != true)
        {
            LogInvalidLogoutToken(logger);
            return Results.BadRequest();
        }

        var sessionId = validation.ClaimsIdentity.FindFirst("sid")?.Value;
        var subject = validation.ClaimsIdentity.FindFirst("sub")?.Value;
        if (sessionId is null && subject is null)
        {
            return Results.BadRequest();
        }

        var removed = await ticketStore.RemoveBySessionAsync(sessionId, subject, context.RequestAborted);
        LogSessionsRevoked(logger, removed);
        return Results.Ok();
    }

    /// <summary>
    /// Handler of <c>GET /bff/logout?sid={sid}</c>: ends the session only when <paramref name="sid"/> equals the session's <c>sid</c> claim
    /// (CSRF protection of a navigation that cannot carry the <c>X-CSRF</c> header), as described on <see cref="SuperApp.Gateway.Bff.BffEndpoints"/>.
    /// </summary>
    /// <param name="context">The current request; <see cref="HttpContext.User"/> is the session principal, or anonymous without a session.</param>
    /// <param name="sid">The <c>sid</c> query parameter taken from <c>logoutUrl</c> of <c>/bff/user</c>; <see langword="null"/> when missing.</param>
    /// <returns>
    /// A redirect to <c>/</c> without a session; 400 Bad Request (nothing is signed out) when <paramref name="sid"/> is missing, the session
    /// has no <c>sid</c> claim or the values differ; otherwise a sign-out of the cookie and OIDC schemes that ends with the redirect to the
    /// CIAM end-session endpoint and back to <c>/</c>.
    /// </returns>
    internal static IResult Logout(HttpContext context, string? sid)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Redirect("/");
        }

        var sessionSid = context.User.FindFirst("sid")?.Value;
        if (sid is null || sessionSid is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sid), Encoding.UTF8.GetBytes(sessionSid)))
        {
            return Results.BadRequest();
        }

        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
    }

    /// <summary>Builds the <c>logoutUrl</c> returned by <c>/bff/user</c> for the given session principal.</summary>
    /// <param name="user">The session principal.</param>
    /// <returns><c>/bff/logout?sid=...</c> with the URL-encoded <c>sid</c> claim, or <see langword="null"/> when the principal has no <c>sid</c> claim.</returns>
    internal static string? LogoutUrl(ClaimsPrincipal user) =>
        user.FindFirst("sid")?.Value is { } sid ? "/bff/logout?sid=" + Uri.EscapeDataString(sid) : null;

    // Accepts only same-origin paths; "//host" and "/\host" are protocol-relative URLs that browsers treat as another origin.
    private static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal);

    [LoggerMessage(3201, LogLevel.Warning, "Back-channel logout rejected: invalid logout token")]
    private static partial void LogInvalidLogoutToken(ILogger logger);

    [LoggerMessage(3202, LogLevel.Information, "Back-channel logout revoked {Count} session(s)")]
    private static partial void LogSessionsRevoked(ILogger logger, int count);
}
