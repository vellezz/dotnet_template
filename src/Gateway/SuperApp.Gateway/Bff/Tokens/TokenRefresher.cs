using SuperApp.Gateway.Bff.Sessions;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace SuperApp.Gateway.Bff.Tokens;

/// <summary>
/// Refreshes the access token stored in a BFF session shortly before it expires (own implementation, ADR-0011), so that the token
/// <see cref="SuperApp.Gateway.Proxy.Transforms.SecurityTransforms"/> attaches to proxied requests is always valid.
/// </summary>
/// <remarks>
/// <para>
/// Hooked into the cookie handler's <c>OnValidatePrincipal</c> event in <c>Program.cs</c>, so it runs on every request that has a session.
/// When the <c>expires_at</c> token of the session is less than 60 seconds away (or already past), it calls the CIAM token endpoint with
/// the <c>refresh_token</c> grant and the confidential client credentials of <c>bff-web</c> (client id and secret taken from the OpenID Connect
/// options). On success the new access token, expiry and (if the CIAM rotated it) refresh token replace the old ones in the stored session
/// and in the current request.
/// </para>
/// <para>
/// <b>Exactly one refresh per session, across all replicas.</b> With refresh token rotation a second use of the same refresh token is treated
/// by the CIAM as token theft and ends the session, so two requests must never refresh with the same token:
/// </para>
/// <list type="number">
///   <item><description>Within a replica, concurrent requests of the same session share one refresh (single-flight per session key).</description></item>
///   <item><description>Across replicas, the refresh runs inside <see cref="SuperApp.Gateway.Bff.Sessions.DbTicketStore.UpdateExclusiveAsync"/>: it holds the
///   session's SQL Server application lock, reads the stored ticket again and calls the CIAM only if the stored access token still needs a
///   refresh. If another replica has already refreshed it, the stored tokens are simply taken over. The new tokens are saved before the lock
///   is released.</description></item>
/// </list>
/// <para>
/// The shared refresh is not bound to the request that started it: it runs with its own cancellation token limited to
/// <see cref="RefreshTimeout"/>, so an aborted request does not fail the other requests waiting for the same refresh.
/// </para>
/// <para>
/// If the CIAM rejects the refresh (for example because the SSO session has ended), the response cannot be read, or the session no longer
/// exists, the principal is rejected and the session is signed out; the request continues unauthenticated and the SPA gets 401 and sends
/// the user to <c>/bff/login</c>. If the refresh times out or the session lock is not granted within
/// <see cref="SuperApp.Gateway.Bff.Sessions.DbTicketStore.LockTimeout"/>, the session is kept and the request continues with the current tokens; the next
/// request tries again. Sessions without a refresh token are never refreshed and simply use the access token until it expires.
/// </para>
/// </remarks>
/// <param name="httpClientFactory">Creates the <see cref="HttpClientName"/> client used to call the token endpoint.</param>
/// <param name="oidcOptions">OpenID Connect options of the <c>bff-web</c> client (discovery document, client id and secret).</param>
/// <param name="ticketStore">Session store that provides the cross-replica lock and stores the refreshed tokens.</param>
/// <param name="timeProvider">Clock used to decide when to refresh and to compute the new expiry.</param>
/// <param name="logger">Logger for rejected, failed and skipped refreshes.</param>
internal sealed partial class TokenRefresher(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
    DbTicketStore ticketStore,
    TimeProvider timeProvider,
    ILogger<TokenRefresher> logger)
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> client used for the token endpoint (registered in <c>Program.cs</c>).</summary>
    public const string HttpClientName = "bff-token-refresh";

    /// <summary>
    /// Upper bound of one shared refresh (waiting for the session lock, reading the discovery document and calling the token endpoint).
    /// Shorter than <see cref="SuperApp.Gateway.Bff.Sessions.DbTicketStore.LockTimeout"/>, so a replica waiting for the lock is not starved by a hanging refresh.
    /// </summary>
    public static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, Lazy<Task<RefreshOutcome>>> _inFlight = new();

    /// <summary>
    /// Handler of the cookie <c>OnValidatePrincipal</c> event: refreshes the session tokens when the access token expires within 60 seconds,
    /// or signs the session out when refreshing fails, as described on <see cref="SuperApp.Gateway.Bff.Tokens.TokenRefresher"/>.
    /// </summary>
    /// <param name="context">
    /// The cookie validation context with the session's principal and stored tokens; its properties must contain the session key
    /// (<see cref="SuperApp.Gateway.Bff.Sessions.DbTicketStore.SessionKeyItem"/>, added by <see cref="SuperApp.Gateway.Bff.Sessions.DbTicketStore.RetrieveAsync"/>),
    /// otherwise nothing is refreshed.
    /// </param>
    /// <returns>A task that completes when the tokens have been checked and, if needed, refreshed or the session rejected.</returns>
    public async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        if (!NeedsRefresh(context.Properties)
            || !context.Properties.Items.TryGetValue(DbTicketStore.SessionKeyItem, out var sessionKey)
            || sessionKey is null)
        {
            return;
        }

        var refresh = _inFlight.GetOrAdd(sessionKey, key => new Lazy<Task<RefreshOutcome>>(() => RefreshSessionAsync(key)));
        RefreshOutcome outcome;
        try
        {
            outcome = await refresh.Value;
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<RefreshOutcome>>>(sessionKey, refresh));
        }

        if (outcome.Tokens is { } tokens)
        {
            context.Properties.StoreTokens(tokens);
        }
        else if (outcome.SignOut)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    // The shared refresh of one session: bounded by its own timeout (not by any request), serialized across replicas by the session lock.
    private async Task<RefreshOutcome> RefreshSessionAsync(string sessionKey)
    {
        using var timeout = new CancellationTokenSource(RefreshTimeout, timeProvider);
        IReadOnlyList<AuthenticationToken>? tokens = null;
        try
        {
            var locked = await ticketStore.UpdateExclusiveAsync(
                sessionKey,
                async (stored, cancellationToken) =>
                {
                    if (stored is null)
                    {
                        return null;
                    }

                    if (!NeedsRefresh(stored.Properties))
                    {
                        // Another replica (or an earlier request) has already refreshed the session: take over its tokens.
                        tokens = stored.Properties.GetTokens().ToList();
                        return null;
                    }

                    var refreshed = await RequestTokensAsync(stored.Properties.GetTokenValue("refresh_token")!, cancellationToken);
                    if (refreshed is null)
                    {
                        return null;
                    }

                    stored.Properties.UpdateTokenValue("access_token", refreshed.AccessToken);
                    stored.Properties.UpdateTokenValue("expires_at", refreshed.ExpiresAt.ToString("o", CultureInfo.InvariantCulture));
                    if (refreshed.RefreshToken is not null)
                    {
                        stored.Properties.UpdateTokenValue("refresh_token", refreshed.RefreshToken);
                    }

                    tokens = stored.Properties.GetTokens().ToList();
                    return stored;
                },
                timeout.Token);

            if (!locked)
            {
                LogSessionLocked(logger, DbTicketStore.LockTimeout);
                return new RefreshOutcome(null, SignOut: false);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            LogRefreshTimedOut(logger, RefreshTimeout);
            return new RefreshOutcome(null, SignOut: false);
        }

        return new RefreshOutcome(tokens, SignOut: tokens is null);
    }

    // True when the ticket has a refresh token and its access token expires within RefreshBeforeExpiry (or has expired).
    private bool NeedsRefresh(AuthenticationProperties properties) =>
        properties.GetTokenValue("expires_at") is { } expiresAt
        && properties.GetTokenValue("refresh_token") is not null
        && DateTimeOffset.Parse(expiresAt, CultureInfo.InvariantCulture) - RefreshBeforeExpiry <= timeProvider.GetUtcNow();

    // Calls the token endpoint from the CIAM discovery document. Returns null (= log out) when the CIAM rejects the request or the
    // response cannot be read; other exceptions (including cancellation by the refresh timeout) propagate to RefreshSessionAsync.
    private async Task<TokenSet?> RequestTokensAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var options = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
            var configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);

            using var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret!,
            });
            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .PostAsync(new Uri(configuration.TokenEndpoint), request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogRefreshRejected(logger, (int)response.StatusCode);
                return null;
            }

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = json.RootElement;
            return new TokenSet(
                root.GetProperty("access_token").GetString()!,
                root.TryGetProperty("refresh_token", out var rotated) ? rotated.GetString() : null,
                timeProvider.GetUtcNow().AddSeconds(root.GetProperty("expires_in").GetInt32()));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException)
        {
            LogRefreshFailed(logger, exception);
            return null;
        }
    }

    [LoggerMessage(3101, LogLevel.Warning, "BFF token refresh rejected by CIAM with status {StatusCode}")]
    private static partial void LogRefreshRejected(ILogger logger, int statusCode);

    [LoggerMessage(3102, LogLevel.Warning, "BFF token refresh failed")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(3103, LogLevel.Warning, "BFF token refresh skipped: session lock not granted within {LockTimeout}; keeping current tokens")]
    private static partial void LogSessionLocked(ILogger logger, TimeSpan lockTimeout);

    [LoggerMessage(3104, LogLevel.Warning, "BFF token refresh timed out after {RefreshTimeout}; keeping current tokens")]
    private static partial void LogRefreshTimedOut(ILogger logger, TimeSpan refreshTimeout);

    /// <summary>Tokens returned by a successful call to the token endpoint.</summary>
    /// <param name="AccessToken">The new access token.</param>
    /// <param name="RefreshToken">The rotated refresh token, or <see langword="null"/> when the CIAM keeps the old one.</param>
    /// <param name="ExpiresAt">Absolute expiry of the new access token (now + <c>expires_in</c>).</param>
    private sealed record TokenSet(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

    /// <summary>Result of one shared refresh, applied by every request that waited for it.</summary>
    /// <param name="Tokens">All tokens of the session after the refresh (refreshed here or by another replica); <see langword="null"/> when there are none.</param>
    /// <param name="SignOut">
    /// <see langword="true"/> when the refresh failed for good (rejected by the CIAM, unreadable response, session gone) and the session must
    /// end; <see langword="false"/> with <paramref name="Tokens"/> <see langword="null"/> when it was only skipped (lock not granted, timeout).
    /// </param>
    private sealed record RefreshOutcome(IReadOnlyList<AuthenticationToken>? Tokens, bool SignOut);
}
