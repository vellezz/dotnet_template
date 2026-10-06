namespace SuperApp.Cli.LocalEnvironment;

/// <summary>Decoded OIDC claims of an access token.</summary>
/// <param name="RawToken">The encoded JWT string.</param>
/// <param name="Issuer">The issuer (<c>iss</c>) claim.</param>
/// <param name="Subject">The subject identifier (<c>sub</c>) claim.</param>
/// <param name="Username">The preferred username (<c>preferred_username</c>) claim.</param>
/// <param name="Audiences">List of target audiences (<c>aud</c>).</param>
/// <param name="Scopes">List of authorized scopes (<c>scope</c>).</param>
/// <param name="Roles">List of granted realm roles (<c>realm_access.roles</c>).</param>
/// <param name="ExpiresAt">Token expiration timestamp (<c>exp</c>).</param>
internal sealed record DecodedToken(
    string RawToken,
    string Issuer,
    string Subject,
    string Username,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Roles,
    DateTimeOffset ExpiresAt);
