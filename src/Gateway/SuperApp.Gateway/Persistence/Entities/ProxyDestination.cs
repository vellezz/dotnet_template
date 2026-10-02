namespace SuperApp.Gateway.Persistence.Entities;

/// <summary>
/// Row of the temporal table <c>gateway.Destinations</c>: an address the gateway forwards to for a YARP cluster.
/// Key: (<see cref="Profile"/>, <see cref="ClusterId"/>, <see cref="DestinationId"/>); FK to <see cref="SuperApp.Gateway.Persistence.Entities.ProxyCluster"/> (ADR-0022).
/// </summary>
/// <remarks>
/// Normally one destination per cluster: the Kubernetes Service of the domain service's API. The CHECK constraint on
/// <see cref="Address"/> guarantees that a route can only point inside the cluster, never to an external host.
/// </remarks>
public sealed class ProxyDestination
{
    /// <summary>Gateway profile (<see cref="SuperApp.Gateway.Hosting.GatewayProfiles"/>); a CHECK constraint allows only <c>bff-web</c> and <c>gateway-mobile</c>. Max. 20 characters.</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>Cluster the address belongs to (FK within the same profile); max. 100 characters.</summary>
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>Id of the address within the cluster; max. 100 characters. Convention: <c>{service}-api</c>.</summary>
    public string DestinationId { get; set; } = string.Empty;

    /// <summary>
    /// Address of the Kubernetes Service (max. 500 characters), e.g. <c>http://knowledge-api.knowledge.svc.cluster.local:8080</c>.
    /// Only the form <c>http://{service}.{namespace}.svc.cluster.local:{port}</c> with an optional trailing <c>/</c> is allowed (no user info,
    /// path, query or fragment): the CHECK constraint <c>CK_Destinations_ClusterAddress</c> enforces it in the database and
    /// <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyDestinationAddress"/> again before a configuration is applied. Load balancing between pods is done by Kubernetes.
    /// </summary>
    public string Address { get; set; } = string.Empty;
}
