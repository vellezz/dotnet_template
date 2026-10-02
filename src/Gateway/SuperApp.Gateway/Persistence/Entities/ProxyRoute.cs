namespace SuperApp.Gateway.Persistence.Entities;

/// <summary>
/// Row of the temporal table <c>gateway.Routes</c>: a YARP route in one gateway profile, i.e. which incoming paths go to which cluster
/// and under which policies. Key: (<see cref="Profile"/>, <see cref="RouteId"/>); FK to <see cref="SuperApp.Gateway.Persistence.Entities.ProxyCluster"/>;
/// unique index (<see cref="Profile"/>, <see cref="Path"/>, <see cref="Order"/>) (ADR-0022).
/// </summary>
/// <remarks>
/// Method and host restrictions and transforms are separate tables (<see cref="SuperApp.Gateway.Persistence.Entities.ProxyRouteMethod"/>, <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRouteHost"/>,
/// <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRouteTransform"/>). Policies are referenced by name only; their definitions are code (<see cref="SuperApp.Gateway.Security.GatewayPolicies"/>,
/// <see cref="SuperApp.Gateway.Security.GatewayRateLimits"/>), and a name that does not exist in code makes the YARP validator reject the whole configuration.
/// </remarks>
public sealed class ProxyRoute
{
    /// <summary>Gateway profile (<see cref="SuperApp.Gateway.Hosting.GatewayProfiles"/>); a CHECK constraint allows only <c>bff-web</c> and <c>gateway-mobile</c>. Max. 20 characters.</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>Route id, unique within the profile; max. 100 characters. Convention: <c>{service}-api</c>.</summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>Cluster that serves the route (FK within the same profile); max. 100 characters.</summary>
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>YARP match priority; routes with a lower value are matched first.</summary>
    public int Order { get; set; }

    /// <summary>
    /// YARP path pattern, e.g. <c>/api/knowledge/{**rest}</c>; a CHECK constraint requires a leading <c>/</c>. Max. 500 characters.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Name of an authorization policy defined in code (<see cref="SuperApp.Gateway.Security.GatewayPolicies"/>); NOT NULL, max. 100 characters. A public route must
    /// explicitly name <see cref="SuperApp.Gateway.Security.GatewayPolicies.Anonymous"/>.
    /// </summary>
    public string AuthorizationPolicy { get; set; } = string.Empty;

    /// <summary>
    /// Name of a rate limiting policy defined in code (e.g. <see cref="SuperApp.Gateway.Security.GatewayRateLimits.PerUser"/>); <see langword="null"/> means no limit.
    /// Max. 100 characters.
    /// </summary>
    public string? RateLimiterPolicy { get; set; }

    /// <summary>
    /// Timeout of the whole request in seconds (&gt; 0), enforced by the request timeouts middleware; <see langword="null"/> means no
    /// route-level timeout.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Maximum request body size in bytes (&gt; 0); <see langword="null"/> means the server's default limit.</summary>
    public long? MaxRequestBodySize { get; set; }
}
