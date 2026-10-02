using System.Net.Http.Headers;

namespace SuperApp.Framework.Infrastructure.Http.ClientCredentials;

/// <summary>
/// HTTP message handler that sets <c>Authorization: Bearer</c> on each outgoing request with a cached client credentials token (ADR-0011, ADR-0014).
/// </summary>
/// <remarks>Added to a client by <see cref="ClientCredentialsHttpClientBuilderExtensions.AddClientCredentialsToken"/>; you do not create it yourself.</remarks>
/// <param name="tokenProvider">Shared provider that obtains and caches tokens.</param>
/// <param name="clientName">Name of the OAuth client whose token is attached.</param>
public sealed class ClientCredentialsTokenHandler(ClientCredentialsTokenProvider tokenProvider, string clientName) : DelegatingHandler
{
    /// <summary>Attaches the current token and forwards the request to the next handler.</summary>
    /// <param name="request">The outgoing request; any existing <c>Authorization</c> header is replaced.</param>
    /// <param name="cancellationToken">Cancellation of the request.</param>
    /// <returns>The response of the inner handler.</returns>
    /// <exception cref="HttpRequestException">The token could not be obtained from the CIAM.</exception>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetTokenAsync(clientName, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
