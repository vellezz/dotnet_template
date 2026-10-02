using SuperApp.Framework.Application.Security;

namespace ServiceName.Application.Tests.Fakes;

/// <summary>
/// <see cref="ICurrentUser"/> with a fixed identity and scopes, for testing the authorization of commands and queries without a token.
/// </summary>
/// <param name="subject">The user's <c>sub</c> claim; <see langword="null"/> simulates an anonymous caller.</param>
/// <param name="scopes">The scopes the user holds, e.g. <c>ServiceNameScopes.Prefix + "things.write"</c>.</param>
internal sealed class FakeCurrentUser(string? subject, params string[] scopes) : ICurrentUser
{
    /// <inheritdoc />
    public bool IsAuthenticated => subject is not null;

    /// <inheritdoc />
    public string? Subject => subject;

    /// <inheritdoc />
    public bool HasScope(string scope) => scopes.Contains(scope);
}
