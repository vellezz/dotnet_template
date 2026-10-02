using SuperApp.Framework.Application.Security;

namespace ServiceName.IntegrationTests.Infrastructure;

/// <summary>
/// Mutable <see cref="ICurrentUser"/> shared by all integration tests through <see cref="ServiceFixture.CurrentUser"/>.
/// </summary>
/// <remarks>
/// The fixture is shared by the whole test assembly, so a test that changes <see cref="Subject"/> or <see cref="Scopes"/> must set exactly
/// what it needs at its start and must not assume the values left by another test.
/// </remarks>
public sealed class TestCurrentUser : ICurrentUser
{
    /// <summary>Gets or sets the user's <c>sub</c> claim; <c>test-user</c> by default, <see langword="null"/> simulates an anonymous caller.</summary>
    public string? Subject { get; set; } = "test-user";

    /// <summary>Gets the scopes the user holds; empty by default, so commands and queries requiring a scope are rejected until a test adds it.</summary>
    public HashSet<string> Scopes { get; } = [];

    /// <inheritdoc />
    public bool IsAuthenticated => Subject is not null;

    /// <inheritdoc />
    public bool HasScope(string scope) => Scopes.Contains(scope);
}
