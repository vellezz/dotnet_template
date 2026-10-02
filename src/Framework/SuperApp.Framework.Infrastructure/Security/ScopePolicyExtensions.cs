using Microsoft.AspNetCore.Authorization;

namespace SuperApp.Framework.Infrastructure.Security;

/// <summary>
/// Authorization policies that require a scope, for hosts without the MediatR pipeline, such as a BFF protecting its internal API
/// (ADR-0039, ADR-0040).
/// </summary>
/// <remarks>
/// <para>
/// Domain services check scopes with <c>[RequiresScope]</c> on commands and queries (ADR-0017). A BFF has no commands: its actions call other
/// APIs, which check their own scopes. What the BFF must check itself is access to its <b>internal</b> API
/// (<c>{experience}.internal.*</c>), because that API is meant only for BFFs of other experiences.
/// </para>
/// <para>
/// The scope is read like <c>HttpCurrentUser.HasScope</c> does: from <c>scope</c> or <c>scp</c> claims, space-separated. A request without
/// the scope gets 403.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// builder.Services.AddAuthorizationBuilder()
///     .AddPolicy(ExampleBffScopes.InternalRead, policy =&gt; policy.RequireScope(ExampleBffScopes.InternalRead));
///
/// [Authorize(Policy = ExampleBffScopes.InternalRead)]
/// public sealed class WidgetsController : ControllerBase { ... }
/// </code>
/// </example>
public static class ScopePolicyExtensions
{
    /// <summary>Requires an authenticated user whose token carries <paramref name="scope"/>.</summary>
    /// <param name="policy">The policy being built.</param>
    /// <param name="scope">The required scope, e.g. <c>example.internal.read</c>.</param>
    /// <returns>The same <paramref name="policy"/>, for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireScope(this AuthorizationPolicyBuilder policy, string scope) =>
        policy.RequireAuthenticatedUser().RequireAssertion(context =>
            context.User.Claims
                .Where(claim => claim.Type is "scope" or "scp")
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Contains(scope, StringComparer.Ordinal));
}
