using SuperApp.Framework.Application.Time;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace SuperApp.Framework.Infrastructure.Http.ClientCredentials;

/// <summary>
/// Obtains OAuth client credentials tokens from the CIAM and caches them per client in memory until 60 seconds before expiry (ADR-0011).
/// </summary>
/// <remarks>
/// Registered as a singleton. Concurrent requests for the same client wait for a single token request instead of each calling the CIAM.
/// Use it through <see cref="ClientCredentialsHttpClientBuilderExtensions.AddClientCredentialsToken"/> rather than directly.
/// </remarks>
/// <param name="httpClientFactory">Factory of the HTTP client used to call the token endpoint (<see cref="TokenHttpClientName"/>).</param>
/// <param name="options">Named <see cref="ClientCredentialsOptions"/>, one per client name.</param>
/// <param name="clock">Clock used to compute token expiry.</param>
public sealed class ClientCredentialsTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClientCredentialsOptions> options,
    IClock clock)
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> client used to call the CIAM token endpoint; configure it to add proxies or timeouts.</summary>
    public const string TokenHttpClientName = "client-credentials-token";

    private static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, CachedToken> _tokens = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>Returns a valid access token for <paramref name="clientName"/>, requesting a new one when the cached token expires within 60 seconds.</summary>
    /// <param name="clientName">Name of the client; selects the named <see cref="ClientCredentialsOptions"/>.</param>
    /// <param name="cancellationToken">Cancellation of the calling request.</param>
    /// <returns>The access token, to be sent as <c>Authorization: Bearer</c>.</returns>
    /// <exception cref="HttpRequestException">The token endpoint is unreachable or returned an error (for example a wrong secret).</exception>
    /// <exception cref="InvalidOperationException">The token endpoint returned an empty response.</exception>
    public async Task<string> GetTokenAsync(string clientName, CancellationToken cancellationToken)
    {
        if (TryGetValid(clientName, out var token))
        {
            return token;
        }

        var gate = _locks.GetOrAdd(clientName, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGetValid(clientName, out token))
            {
                return token;
            }

            var settings = options.Get(clientName);
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret,
            };
            if (!string.IsNullOrWhiteSpace(settings.Scope))
            {
                form["scope"] = settings.Scope;
            }

            using var content = new FormUrlEncodedContent(form);
            using var response = await httpClientFactory.CreateClient(TokenHttpClientName)
                .PostAsync(settings.TokenEndpoint, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Pusta odpowiedź endpointu tokenów.");

            _tokens[clientName] = new CachedToken(tokenResponse.AccessToken, clock.UtcNow.AddSeconds(tokenResponse.ExpiresIn));
            return tokenResponse.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGetValid(string clientName, out string token)
    {
        if (_tokens.TryGetValue(clientName, out var cached) && cached.ExpiresAt - RefreshBeforeExpiry > clock.UtcNow)
        {
            token = cached.AccessToken;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
