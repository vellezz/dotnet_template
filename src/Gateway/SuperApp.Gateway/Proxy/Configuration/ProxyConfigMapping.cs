using Yarp.ReverseProxy.Configuration;

namespace SuperApp.Gateway.Proxy.Configuration;

/// <summary>
/// Output of <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigMapper.Map"/>: the YARP routes and clusters built from one
/// <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigSnapshot"/>, together with the problems found while building them (ADR-0022).
/// </summary>
/// <remarks>
/// A configuration with at least one entry in <see cref="Errors"/> is invalid as a whole and must not be applied;
/// <see cref="SuperApp.Gateway.Proxy.Configuration.DatabaseProxyConfigProvider"/> treats these errors exactly like errors of YARP's validator
/// (event 3002, the previous configuration stays active). <see cref="Routes"/> and <see cref="Clusters"/> are still filled so that YARP's
/// validator can report its own findings for the same version in one log entry.
/// </remarks>
/// <param name="Routes">One YARP route per route row; transforms that could not be mapped are left out.</param>
/// <param name="Clusters">One YARP cluster per cluster row; destinations with an invalid address are left out.</param>
/// <param name="Errors">
/// Human-readable descriptions of the rows the gateway does not accept (unsupported transform kind, missing transform field, destination
/// outside the cluster, invalid HTTP version); empty when the snapshot was mapped completely.
/// </param>
internal sealed record ProxyConfigMapping(
    IReadOnlyList<RouteConfig> Routes,
    IReadOnlyList<ClusterConfig> Clusters,
    IReadOnlyList<string> Errors)
{
    /// <summary>Whether the snapshot was mapped without errors (YARP's validator may still reject it).</summary>
    public bool IsValid => Errors.Count == 0;
}
