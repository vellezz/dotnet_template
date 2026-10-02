using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace SuperApp.Gateway.Security;

/// <summary>
/// Coarse-grained, per-route authorization policies of the gateway. The policies are defined here in code; a route row in the database
/// only references a policy by name in <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRoute.AuthorizationPolicy"/> (ADR-0012, ADR-0022).
/// </summary>
/// <remarks>
/// <para>
/// The gateway only answers the question "may this caller talk to service X at all?" by checking that the token carries at least one scope
/// of that service (scope convention <c>{service}.{resource}.{action}</c>). Fine-grained rules (which resource, owner checks, business rules)
/// belong to the domain service, which also validates the token itself (ADR-0007). The gateway must never know domain rules.
/// </para>
/// <para>
/// Both profiles evaluate the same <c>scope</c>/<c>scp</c> claims: in <c>gateway-mobile</c> they come from the JWT; in <c>bff-web</c>
/// the OIDC handler copies the granted scope from the token response into the session (see <c>OnTokenValidated</c> in <c>Program.cs</c>).
/// </para>
/// <para>
/// Adding a new experience: add a constant and an entry in <c>ServiceScopePrefixes</c> here, reference the name from the route seed
/// (<see cref="SuperApp.Gateway.Persistence.Seed.ProxyConfigurationSeed"/>) and create a gateway migration; adding a domain service to an
/// experience: add its scope prefix to the experience's entry. <c>dotnet superapp add bff</c> and <c>add service</c> do both (ADR-0046).
/// A route that names a policy that is not registered is rejected by the YARP validator and the previous configuration stays active.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Program.cs: every endpoint without an explicit policy requires an authenticated user
/// GatewayPolicies.Register(builder.Services.AddAuthorizationBuilder()
///     .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
///
/// // ProxyConfigurationSeed.cs: the route references the policy by name
/// new ProxyRoute { RouteId = "example-bff", AuthorizationPolicy = GatewayPolicies.Example, ... }
/// </code>
/// </example>
public static class GatewayPolicies
{
    /// <summary>
    /// Name reserved by YARP for routes reachable without authentication. A public route must reference it explicitly in
    /// <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRoute.AuthorizationPolicy"/> (the column is NOT NULL, so a route never becomes public by omission);
    /// it is not registered in <see cref="Register"/>.
    /// </summary>
    public const string Anonymous = "anonymous";

    /// <summary>
    /// Access to the public API of the Example experience (its BFF, ADR-0038): an authenticated user with at least one scope of the experience's
    /// domain services (<c>knowledge.*</c> or <c>sleepdiary.*</c>). The BFF and the services check the exact scopes.
    /// </summary>
    public const string Example = "example";

    // Scope prefixes of the domain services of each experience: one line per experience, edited by `dotnet superapp add|remove service`.
    // An experience without services yet has an empty list, so its route rejects every caller until a service is added.
    private static readonly Dictionary<string, string[]> ServiceScopePrefixes = new(StringComparer.Ordinal)
    {
        [Example] = ["knowledge.", "sleepdiary."],
    };

    /// <summary>
    /// Registers one policy per experience of <c>ServiceScopePrefixes</c>: an authenticated user with at least one scope of one of the
    /// experience's domain services. Scopes are read from the <c>scope</c> or <c>scp</c> claims (space-separated values);
    /// the prefix comparison is case-sensitive. The gateway only checks that the token has any permission for the service;
    /// fine-grained rules belong to the domain service (ADR-0012).
    /// </summary>
    /// <param name="authorization">The application's authorization builder the policies are added to.</param>
    public static void Register(AuthorizationBuilder authorization)
    {
        foreach (var (experience, prefixes) in ServiceScopePrefixes)
        {
            authorization.AddPolicy(experience, policy => policy.RequireAuthenticatedUser()
                .RequireAssertion(context => prefixes.Any(prefix => HasScopeOf(context.User, prefix))));
        }
    }

    private static bool HasScopeOf(ClaimsPrincipal user, string servicePrefix) =>
        user.Claims
            .Where(claim => claim.Type is "scope" or "scp")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Any(scope => scope.StartsWith(servicePrefix, StringComparison.Ordinal));
}
