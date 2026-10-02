namespace SuperApp.Framework.Infrastructure.Http.UserContext;

/// <summary>
/// Supplies the access token sent with a call made in the current user's context, for example from a BFF to a domain service of its
/// experience (ADR-0040).
/// </summary>
/// <remarks>
/// <para>
/// Today the only implementation passes the user's token on unchanged: the JWT access token issued by the CIAM that the current request
/// arrived with. The token already carries the audiences of the experience's BFF and services (ADR-0012), so the callee validates it like a
/// token from the edge gateway and applies its own scope and resource rules.
/// </para>
/// <para>
/// This interface is the seam for the token exchange left open by ADR-0040 (RFC 8693): an implementation that exchanges the incoming token
/// for one with the callee's audience only and the caller as the actor can replace the default registration without any change to BFFs,
/// services or contracts. Code that makes calls never uses this interface directly; it registers a client with
/// <c>AddUserTokenForwarding</c>.
/// </para>
/// </remarks>
public interface IDownstreamTokenProvider
{
    /// <summary>Returns the access token to send with a call made in the current user's context.</summary>
    /// <param name="cancellationToken">Cancellation of the outgoing call.</param>
    /// <returns>
    /// The raw JWT (without the <c>Bearer</c> prefix), or <see langword="null"/> when the current request has no user token, for example
    /// an anonymous request or a call outside an HTTP request; the call then goes without an <c>Authorization</c> header and the callee
    /// rejects it unless the operation is anonymous.
    /// </returns>
    ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken);
}
