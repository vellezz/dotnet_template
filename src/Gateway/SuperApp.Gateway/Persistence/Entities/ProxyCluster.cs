namespace SuperApp.Gateway.Persistence.Entities;

// YARP configuration stored in relational tables, per gateway profile (ADR-0022). Changed only through migrations (HasData).

/// <summary>
/// Row of the temporal table <c>gateway.Clusters</c>: a YARP cluster (one domain service) in one gateway profile.
/// Key: (<see cref="Profile"/>, <see cref="ClusterId"/>). Changed only through migrations (ADR-0022).
/// </summary>
/// <remarks>
/// Rows are defined in <see cref="SuperApp.Gateway.Persistence.Seed.ProxyConfigurationSeed"/> and mapped to YARP's <c>ClusterConfig</c> by <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigMapper"/>;
/// the addresses of the cluster are rows of <see cref="SuperApp.Gateway.Persistence.Entities.ProxyDestination"/>. Constraints are defined in <see cref="SuperApp.Gateway.Persistence.Configurations.ProxyClusterConfiguration"/>.
/// </remarks>
public sealed class ProxyCluster
{
    /// <summary>Gateway profile (<see cref="SuperApp.Gateway.Hosting.GatewayProfiles"/>); a CHECK constraint allows only <c>bff-web</c> and <c>gateway-mobile</c>. Max. 20 characters.</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>YARP cluster id, unique within the profile; max. 100 characters. Convention: the service name, e.g. <c>knowledge</c>.</summary>
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>
    /// YARP load balancing policy (<c>RoundRobin</c>, <c>PowerOfTwoChoices</c>, <c>LeastRequests</c>, <c>Random</c>,
    /// <c>FirstAlphabetical</c>); <see langword="null"/> means YARP's default. Max. 30 characters. With a single Kubernetes Service
    /// destination per cluster the real balancing between pods is done by Kubernetes.
    /// </summary>
    public string? LoadBalancingPolicy { get; set; }

    /// <summary>
    /// How long, in seconds, a request to the service may stay without any activity (no data sent or received) before YARP cancels it;
    /// a CHECK constraint requires a value &gt; 0.
    /// </summary>
    public int ActivityTimeoutSeconds { get; set; }

    /// <summary>HTTP version used towards the service: <c>1.1</c> or <c>2</c> (CHECK); <see langword="null"/> means YARP's default.</summary>
    public string? HttpVersion { get; set; }

    /// <summary>Whether YARP actively probes the health of the destinations; requires <see cref="HealthCheckPath"/> (CHECK).</summary>
    public bool HealthCheckEnabled { get; set; }

    /// <summary>Path of the active health check (max. 200 characters); required when <see cref="HealthCheckEnabled"/> is set.</summary>
    public string? HealthCheckPath { get; set; }

    /// <summary>Interval between health probes in seconds (&gt; 0); <see langword="null"/> means YARP's default.</summary>
    public int? HealthCheckIntervalSeconds { get; set; }

    /// <summary>Timeout of a single health probe in seconds (&gt; 0); <see langword="null"/> means YARP's default.</summary>
    public int? HealthCheckTimeoutSeconds { get; set; }
}
