using SuperApp.Gateway.Persistence.Entities;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace SuperApp.Gateway.Proxy.Configuration;

/// <summary>
/// Maps the rows of the route configuration tables (<see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigSnapshot"/>) to YARP's <see cref="RouteConfig"/> and
/// <see cref="ClusterConfig"/>. The single place that defines which subset of YARP options the gateway supports (ADR-0022).
/// </summary>
/// <remarks>
/// <para>
/// Supporting a new YARP option means: a new column or table (entity + <c>IEntityTypeConfiguration</c> + migration) and its mapping here.
/// </para>
/// <para>
/// The mapper checks what only the gateway knows and reports it in <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigMapping.Errors"/> instead of
/// throwing: unsupported transform kinds, transform rows without the field their kind requires, destination addresses outside the cluster
/// (<see cref="SuperApp.Gateway.Proxy.Configuration.ProxyDestinationAddress"/>) and invalid HTTP versions. The database constraints normally prevent all of
/// these; the check here makes sure that a row that slipped through (disabled constraint, cached snapshot) produces an invalid
/// configuration (event 3002) and never a half-applied one. Everything else (paths, policies, timeouts) is validated afterwards by YARP's
/// validator in <see cref="SuperApp.Gateway.Proxy.Configuration.DatabaseProxyConfigProvider"/>.
/// </para>
/// <para>
/// Mapping rules: seconds columns become <see cref="TimeSpan"/>s; a route without method or host rows matches any method or host
/// (<see langword="null"/> in <see cref="RouteMatch"/>); transforms are applied in ascending <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRouteTransform.Order"/>;
/// active health checks are configured only when <see cref="SuperApp.Gateway.Persistence.Entities.ProxyCluster.HealthCheckEnabled"/> is set.
/// </para>
/// </remarks>
internal static class ProxyConfigMapper
{
    /// <summary>Builds the YARP routes and clusters of the snapshot and collects the rows the gateway does not accept.</summary>
    /// <param name="snapshot">All configuration rows of one profile.</param>
    /// <returns>
    /// One <see cref="RouteConfig"/> per <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRoute"/> row, one <see cref="ClusterConfig"/> per
    /// <see cref="SuperApp.Gateway.Persistence.Entities.ProxyCluster"/> row and the list of errors; the configuration may be applied only when
    /// <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigMapping.IsValid"/> is <see langword="true"/>.
    /// </returns>
    public static ProxyConfigMapping Map(ProxyConfigSnapshot snapshot)
    {
        var errors = new List<string>();
        var routes = snapshot.Routes.Select(route => ToRoute(snapshot, route, errors)).ToList();
        var clusters = snapshot.Clusters.Select(cluster => ToCluster(snapshot, cluster, errors)).ToList();
        return new ProxyConfigMapping(routes, clusters, errors);
    }

    private static RouteConfig ToRoute(ProxyConfigSnapshot snapshot, ProxyRoute route, List<string> errors) => new()
    {
        RouteId = route.RouteId,
        ClusterId = route.ClusterId,
        Order = route.Order,
        AuthorizationPolicy = route.AuthorizationPolicy,
        RateLimiterPolicy = route.RateLimiterPolicy,
        Timeout = route.TimeoutSeconds is { } timeout ? TimeSpan.FromSeconds(timeout) : null,
        MaxRequestBodySize = route.MaxRequestBodySize,
        Match = new RouteMatch
        {
            Path = route.Path,
            Methods = NullIfEmpty(snapshot.Methods.Where(method => method.RouteId == route.RouteId).Select(method => method.Method).ToList()),
            Hosts = NullIfEmpty(snapshot.Hosts.Where(host => host.RouteId == route.RouteId).Select(host => host.Host).ToList()),
        },
        Transforms = snapshot.Transforms
            .Where(transform => transform.RouteId == route.RouteId)
            .OrderBy(transform => transform.Order)
            .Select(transform => ToTransform(transform, errors))
            .OfType<Dictionary<string, string>>()
            .ToList(),
    };

    private static ClusterConfig ToCluster(ProxyConfigSnapshot snapshot, ProxyCluster cluster, List<string> errors)
    {
        Version? httpVersion = null;
        if (cluster.HttpVersion is not null && !Version.TryParse(cluster.HttpVersion, out httpVersion))
        {
            errors.Add($"Cluster '{cluster.ClusterId}': invalid HTTP version '{cluster.HttpVersion}'.");
        }

        var destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var destination in snapshot.Destinations.Where(destination => destination.ClusterId == cluster.ClusterId))
        {
            if (ProxyDestinationAddress.IsValid(destination.Address))
            {
                destinations[destination.DestinationId] = new DestinationConfig { Address = destination.Address };
            }
            else
            {
                errors.Add($"Cluster '{cluster.ClusterId}', destination '{destination.DestinationId}': address '{destination.Address}' is not "
                    + "http://{service}.{namespace}.svc.cluster.local:{port}.");
            }
        }

        return new ClusterConfig
        {
            ClusterId = cluster.ClusterId,
            LoadBalancingPolicy = cluster.LoadBalancingPolicy,
            HttpRequest = new ForwarderRequestConfig
            {
                ActivityTimeout = TimeSpan.FromSeconds(cluster.ActivityTimeoutSeconds),
                Version = httpVersion,
            },
            HealthCheck = cluster.HealthCheckEnabled
                ? new HealthCheckConfig
                {
                    Active = new ActiveHealthCheckConfig
                    {
                        Enabled = true,
                        Path = cluster.HealthCheckPath,
                        Interval = cluster.HealthCheckIntervalSeconds is { } interval ? TimeSpan.FromSeconds(interval) : null,
                        Timeout = cluster.HealthCheckTimeoutSeconds is { } timeout ? TimeSpan.FromSeconds(timeout) : null,
                    },
                }
                : null,
            Destinations = destinations,
        };
    }

    // Translates a transform row into YARP's key/value transform syntax; the supported kinds match the CK_RouteTransforms_Kind constraint
    // and the required fields match CK_RouteTransforms_Fields. Returns null (and records an error) for a row that cannot be mapped.
    private static Dictionary<string, string>? ToTransform(ProxyRouteTransform transform, List<string> errors)
    {
        var mapped = transform.Kind switch
        {
            "PathRemovePrefix" when transform.Value is { } value => new Dictionary<string, string> { ["PathRemovePrefix"] = value },
            "PathPrefix" when transform.Value is { } value => new Dictionary<string, string> { ["PathPrefix"] = value },
            "PathPattern" when transform.Value is { } value => new Dictionary<string, string> { ["PathPattern"] = value },
            "RequestHeaderSet" when transform is { Name: { } name, Value: { } value } => new Dictionary<string, string> { ["RequestHeader"] = name, ["Set"] = value },
            "RequestHeaderRemove" when transform.Name is { } name => new Dictionary<string, string> { ["RequestHeaderRemove"] = name },
            "ResponseHeaderSet" when transform is { Name: { } name, Value: { } value } => new Dictionary<string, string> { ["ResponseHeader"] = name, ["Set"] = value },
            "ResponseHeaderRemove" when transform.Name is { } name => new Dictionary<string, string> { ["ResponseHeaderRemove"] = name },
            _ => null,
        };

        if (mapped is null)
        {
            errors.Add($"Route '{transform.RouteId}', transform {transform.Order}: unsupported kind '{transform.Kind}' or missing Name/Value.");
        }

        return mapped;
    }

    private static List<string>? NullIfEmpty(List<string> values) => values.Count == 0 ? null : values;
}
