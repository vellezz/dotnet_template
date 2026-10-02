using SuperApp.Gateway.Hosting;
using SuperApp.Gateway.Persistence.Entities;
using SuperApp.Gateway.Security;
using Microsoft.EntityFrameworkCore;

namespace SuperApp.Gateway.Persistence.Seed;

/// <summary>
/// The routes and clusters of both gateway profiles, declared as model data (<c>HasData</c>). This file is the source of truth of the
/// gateway routing: EF compares it with the model snapshot and generates the <c>INSERT</c>/<c>UPDATE</c>/<c>DELETE</c> statements into the
/// next gateway migration, so every routing change is visible in the diff and reviewed (ADR-0022).
/// </summary>
/// <remarks>
/// <para>
/// Every experience has one route, to its BFF (ADR-0038, ADR-0039); domain services have none, because only the BFF of their experience calls
/// them. For every experience in <c>Experiences</c> and every profile it declares:
/// </para>
/// <list type="bullet">
///   <item><description>cluster <c>{experience}</c>: <c>RoundRobin</c>, activity timeout 30 s, HTTP/1.1;</description></item>
///   <item><description>destination <c>{experience}-bff</c> at <c>http://{experience}-bff.{experience}.svc.cluster.local:8080</c> (the Kubernetes
///   Service of the BFF; the address is the same on every environment);</description></item>
///   <item><description>route <c>{experience}-bff</c> matching <c>/api/{experience}/v{version:int}/{**rest}</c> with order 100, the experience's
///   policy from <see cref="SuperApp.Gateway.Security.GatewayPolicies"/>, rate limiting <see cref="SuperApp.Gateway.Security.GatewayRateLimits.PerUser"/> and a
///   30 s request timeout. Only the versioned public API matches; the internal API of the BFF (<c>/internal/...</c>) is never routed;</description></item>
///   <item><description>transform <c>PathRemovePrefix /api/{experience}</c>, so <c>/api/example/v1/knowledge/materials</c> reaches the BFF
///   as <c>/v1/knowledge/materials</c>.</description></item>
/// </list>
/// <para>
/// This gateway is the local stand-in for the shared edge gateway of the super app (ADR-0037): these routes are also the requirement for it.
/// </para>
/// <para>
/// To change routing: edit this file, run <c>dotnet ef migrations add &lt;Name&gt; -p src/Gateway/SuperApp.Gateway</c>, review the generated
/// migration and let <c>SuperApp.Migrator</c> or the DBA apply it. The running replicas pick it up within one poll interval
/// (<see cref="SuperApp.Gateway.Proxy.Configuration.DatabaseProxyConfigProvider"/>). Never edit the tables by hand and never edit an applied migration. Keep changes
/// backward compatible with the running gateway version (expand/contract): a route that references a policy that does not exist in the running
/// code is rejected by validation.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Adding a new experience "physio": register GatewayPolicies.Physio in code, then extend the list and create a migration.
/// // A new domain service gets no route: it is called only by the BFF of its experience.
/// internal static readonly (string Experience, string Policy)[] Experiences =
/// [
///     ("example", GatewayPolicies.Example),
///     ("physio", GatewayPolicies.Physio),
/// ];
/// </code>
/// </example>
internal static class ProxyConfigurationSeed
{
    private static readonly string[] Profiles = [GatewayProfiles.BffWeb, GatewayProfiles.Mobile];

    // One route per experience, to its BFF (ADR-0038, ADR-0039). Domain services have no route: only the BFF of their experience calls them.
    // The local gateway stands in for the shared edge gateway (ADR-0037), so these routes are also the requirement for it.
    // One line per experience, edited by `dotnet superapp add|remove bff` (ADR-0046).
    internal static readonly (string Experience, string Policy)[] Experiences =
    [
        ("example", GatewayPolicies.Example),
    ];

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProxyCluster>().HasData(
            from profile in Profiles
            from experience in Experiences
            select new ProxyCluster
            {
                Profile = profile,
                ClusterId = experience.Experience,
                LoadBalancingPolicy = "RoundRobin",
                ActivityTimeoutSeconds = 30,
                HttpVersion = "1.1",
            });

        modelBuilder.Entity<ProxyDestination>().HasData(
            from profile in Profiles
            from experience in Experiences
            select new ProxyDestination
            {
                Profile = profile,
                ClusterId = experience.Experience,
                DestinationId = $"{experience.Experience}-bff",
                Address = $"http://{experience.Experience}-bff.{experience.Experience}.svc.cluster.local:8080",
            });

        // Only the public API of the BFF (/v{n}/...) is routed; /internal/... never matches (ADR-0039).
        modelBuilder.Entity<ProxyRoute>().HasData(
            from profile in Profiles
            from experience in Experiences
            select new ProxyRoute
            {
                Profile = profile,
                RouteId = $"{experience.Experience}-bff",
                ClusterId = experience.Experience,
                Order = 100,
                Path = $"/api/{experience.Experience}/v{{version:int}}/{{**rest}}",
                AuthorizationPolicy = experience.Policy,
                RateLimiterPolicy = GatewayRateLimits.PerUser,
                TimeoutSeconds = 30,
            });

        modelBuilder.Entity<ProxyRouteTransform>().HasData(
            from profile in Profiles
            from experience in Experiences
            select new ProxyRouteTransform
            {
                Profile = profile,
                RouteId = $"{experience.Experience}-bff",
                Order = 0,
                Kind = "PathRemovePrefix",
                Value = $"/api/{experience.Experience}",
            });
    }
}
