namespace SuperApp.Gateway.Persistence.Entities;

/// <summary>
/// Row of the temporal table <c>gateway.RouteTransforms</c>: one YARP request/response transform of a route, from the supported subset.
/// Key: (<see cref="Profile"/>, <see cref="RouteId"/>, <see cref="Order"/>); FK to <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRoute"/>.
/// The security transforms (Bearer token, removing <c>Cookie</c> and <c>X-User-*</c>) are code (<see cref="SuperApp.Gateway.Proxy.Transforms.SecurityTransforms"/>),
/// not rows of this table (ADR-0022).
/// </summary>
/// <remarks>
/// Mapped to YARP's transform dictionaries by <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigMapper"/>. The typical transform is <c>PathRemovePrefix</c>
/// with <see cref="Value"/> <c>/api/{service}</c>, which strips the gateway prefix before the request reaches the service.
/// </remarks>
public sealed class ProxyRouteTransform
{
    /// <summary>Gateway profile (<see cref="SuperApp.Gateway.Hosting.GatewayProfiles"/>); part of the FK to the route. Max. 20 characters.</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>Route the transform belongs to; max. 100 characters.</summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>Position in which the transform is applied within the route (ascending); part of the key.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Kind of transform (max. 30 characters), enforced by CHECK constraints together with the required fields:
    /// <c>PathRemovePrefix</c>, <c>PathPrefix</c>, <c>PathPattern</c> require <see cref="Value"/> and an empty <see cref="Name"/>;
    /// <c>RequestHeaderSet</c>, <c>ResponseHeaderSet</c> require both fields;
    /// <c>RequestHeaderRemove</c>, <c>ResponseHeaderRemove</c> require <see cref="Name"/> and an empty <see cref="Value"/>.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Header name for header transforms; <see langword="null"/> for path transforms. Max. 100 characters.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Path, prefix, pattern or header value; <see langword="null"/> for transforms that remove a header. Max. 500 characters.
    /// </summary>
    public string? Value { get; set; }
}
