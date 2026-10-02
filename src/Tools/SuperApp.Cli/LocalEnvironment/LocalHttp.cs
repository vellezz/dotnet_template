using System.Net.Http.Json;
using System.Text.Json;

namespace SuperApp.Cli.LocalEnvironment;

/// <summary>HTTP access to the local environment: probes, tokens from the local realm and requests of the end-to-end scenario.</summary>
/// <remarks>
/// <para>
/// The gateways use the local development certificate (<c>deploy/local/certs</c>, ADR-0034), which the machine may not trust, so the
/// certificate check is skipped for <c>localhost</c> only; any other host is validated normally. Redirects are not followed, so a test can
/// check a redirect itself (e.g. <c>/bff/login</c> to the CIAM).
/// </para>
/// <para>
/// Tokens come from the resource owner password grant of the local client <c>dev-cli</c> and the local users (<c>reader</c>, <c>editor</c>,
/// password = user name, ADR-0031). This works only against the local realm; the CIAM of other environments has no such client.
/// </para>
/// </remarks>
internal sealed class LocalHttp : IDisposable
{
    private readonly HttpClient _client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None || request.RequestUri?.Host == "localhost",
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    /// <summary>Sends a request.</summary>
    /// <param name="method">HTTP method.</param>
    /// <param name="url">Absolute URL.</param>
    /// <param name="token">Bearer token, or <see langword="null"/> for an anonymous request.</param>
    /// <param name="body">JSON body, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The status code and the body as text; status 0 and the error message when the component cannot be reached.</returns>
    public async Task<(int Status, string Body)> SendAsync(HttpMethod method, string url, string? token = null, object? body = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        try
        {
            using var response = await _client.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return ((int)response.StatusCode, response.Headers.Location is { } location ? location.ToString() : text);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return (0, exception.Message);
        }
    }

    /// <summary>Gets an access token of a local user from the local realm.</summary>
    /// <param name="tokenEndpoint">Token endpoint of the realm.</param>
    /// <param name="user">Local user, e.g. <c>reader</c>.</param>
    /// <param name="password">Password of the user.</param>
    /// <param name="scopes">Requested scopes, e.g. <c>openid knowledge.catalog.read</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The access token and the granted scopes.</returns>
    /// <exception cref="InvalidOperationException">The realm refused the request; the message contains its answer.</exception>
    public async Task<(string Token, string Scope)> TokenAsync(string tokenEndpoint, string user, string password, string scopes, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "dev-cli",
            ["username"] = user,
            ["password"] = password,
            ["scope"] = scopes,
        });
        try
        {
            using var response = await _client.PostAsync(tokenEndpoint, content, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"The local realm refused the token request ({(int)response.StatusCode}): {text}");
            }

            using var document = JsonDocument.Parse(text);
            return (document.RootElement.GetProperty("access_token").GetString()!, document.RootElement.TryGetProperty("scope", out var scope) ? scope.GetString() ?? string.Empty : string.Empty);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException($"The local realm at {tokenEndpoint} cannot be reached ({exception.Message}); start the environment: dotnet superapp env up.");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
