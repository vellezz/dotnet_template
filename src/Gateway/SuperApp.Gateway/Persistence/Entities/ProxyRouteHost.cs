namespace SuperApp.Gateway.Persistence.Entities;

/// <summary>
/// Row of the temporal table <c>gateway.RouteHosts</c>: a host name a route is restricted to; a route without rows matches any host.
/// Key: (<see cref="Profile"/>, <see cref="RouteId"/>, <see cref="Host"/>); FK to <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRoute"/> (ADR-0022).
/// </summary>
public sealed class ProxyRouteHost
{
    /// <summary>Gateway profile (<see cref="SuperApp.Gateway.Hosting.GatewayProfiles"/>); part of the FK to the route. Max. 20 characters.</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>Route the match belongs to; max. 100 characters.</summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>Host name in YARP format (optional port, a leading wildcard such as <c>*.example.com</c> is allowed); max. 255 characters.</summary>
    public string Host { get; set; } = string.Empty;
}
