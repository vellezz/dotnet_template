namespace SuperApp.Framework.Application.Security;

/// <summary>
/// The identity on whose behalf the current command or query runs: the user from the validated access token,
/// or the system identity when a message is processed by the Worker.
/// </summary>
/// <remarks>
/// <para>
/// Inject it into handlers that need the caller's identity for resource-level rules (for example "list my favorites" filters by
/// <see cref="Subject"/>). Scope checks declared with <see cref="RequiresScopeAttribute"/> are already done by the authorization
/// behavior, so handlers rarely need <see cref="HasScope"/> themselves, except for rules like "editors also see drafts".
/// </para>
/// <para>Implementations registered by the framework:</para>
/// <list type="bullet">
///   <item><description>API: reads the JWT validated by the service itself (claims <c>sub</c>, <c>scope</c>/<c>scp</c>); the gateway only forwards the token (ADR-0007).</description></item>
///   <item><description>Worker: a system identity that is always authenticated, has no subject and has every scope. Commands sent by consumers
///   therefore must not depend on <see cref="Subject"/>.</description></item>
/// </list>
/// <para>Never trust identity headers such as <c>X-User-Id</c>; identity comes only from the token.</para>
/// </remarks>
public interface ICurrentUser
{
    /// <summary>Gets a value indicating whether the request carries a valid access token (always <see langword="true"/> for the system identity).</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the stable identifier of the user (claim <c>sub</c> issued by the CIAM), or <see langword="null"/> for anonymous requests and the system identity.
    /// </summary>
    /// <remarks>Use it as the owner key of user data (favorites, diary entries). It is an opaque string; do not parse it.</remarks>
    string? Subject { get; }

    /// <summary>Checks whether the caller's token grants the given scope.</summary>
    /// <param name="scope">Scope name in the <c>{service}.{resource}.{action}</c> convention, e.g. <c>knowledge.catalog.write</c>; compared exactly (ordinal).</param>
    /// <returns>
    /// <see langword="true"/> when the token contains the scope; <see langword="false"/> for anonymous requests.
    /// Always <see langword="true"/> for the system identity.
    /// </returns>
    bool HasScope(string scope);
}
