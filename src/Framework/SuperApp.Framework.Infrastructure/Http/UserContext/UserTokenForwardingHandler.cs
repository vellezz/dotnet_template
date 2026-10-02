using System.Net.Http.Headers;

namespace SuperApp.Framework.Infrastructure.Http.UserContext;

/// <summary>
/// HTTP message handler that sends the current user's token with an outgoing call (ADR-0040); added to a client by <c>AddUserTokenForwarding</c>.
/// </summary>
/// <remarks>
/// The token comes from <see cref="IDownstreamTokenProvider"/>. Any <c>Authorization</c> header set by the caller is replaced, so a client can
/// never send another identity by mistake. Without a user token no header is sent.
/// </remarks>
/// <param name="tokenProvider">Source of the token to send.</param>
internal sealed class UserTokenForwardingHandler(IDownstreamTokenProvider tokenProvider) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetTokenAsync(cancellationToken);
        request.Headers.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
