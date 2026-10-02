namespace SuperApp.Gateway.Persistence.Entities;

/// <summary>
/// Row of the temporal table <c>gateway.RouteMethods</c>: an HTTP method a route is restricted to; a route without rows matches any method.
/// Key: (<see cref="Profile"/>, <see cref="RouteId"/>, <see cref="Method"/>); FK to <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRoute"/> (ADR-0022).
/// </summary>
public sealed class ProxyRouteMethod
{
    /// <summary>Gateway profile (<see cref="SuperApp.Gateway.Hosting.GatewayProfiles"/>); part of the FK to the route. Max. 20 characters.</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>Route the match belongs to; max. 100 characters.</summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method in upper case; a CHECK constraint allows <c>GET</c>, <c>POST</c>, <c>PUT</c>, <c>PATCH</c>, <c>DELETE</c>,
    /// <c>HEAD</c>, <c>OPTIONS</c>. Max. 10 characters.
    /// </summary>
    public string Method { get; set; } = string.Empty;
}
