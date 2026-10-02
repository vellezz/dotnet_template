using SuperApp.Gateway.Persistence.Entities;

namespace SuperApp.Gateway.Proxy.Configuration;

/// <summary>
/// All route configuration rows of one gateway profile, read together at one migration version (ADR-0022). Input of
/// <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigMapper"/>; the last snapshot that passed validation is also stored as JSON in the L2 cache (Redis) so that a replica
/// can start while the database is unavailable.
/// </summary>
/// <remarks>
/// The snapshot is serialized to the cache, so changing its shape or the shape of the entity classes requires bumping the version segment of
/// the cache key in <see cref="SuperApp.Gateway.Proxy.Configuration.DatabaseProxyConfigProvider"/> (<c>gateway:yarp-config:v1:{profile}</c>); otherwise a new version may read
/// an incompatible old entry.
/// </remarks>
/// <param name="MigrationId">
/// Id of the last applied gateway migration at the time the rows were read; it is the configuration version, and a new value triggers a reload.
/// </param>
/// <param name="Clusters">Clusters of the profile.</param>
/// <param name="Destinations">Destination addresses of the profile's clusters.</param>
/// <param name="Routes">Routes of the profile.</param>
/// <param name="Methods">HTTP method matches of the profile's routes; a route without rows matches any method.</param>
/// <param name="Hosts">Host matches of the profile's routes; a route without rows matches any host.</param>
/// <param name="Transforms">Request and response transforms of the profile's routes.</param>
public sealed record ProxyConfigSnapshot(
    string MigrationId,
    IReadOnlyList<ProxyCluster> Clusters,
    IReadOnlyList<ProxyDestination> Destinations,
    IReadOnlyList<ProxyRoute> Routes,
    IReadOnlyList<ProxyRouteMethod> Methods,
    IReadOnlyList<ProxyRouteHost> Hosts,
    IReadOnlyList<ProxyRouteTransform> Transforms);
