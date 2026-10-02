using SuperApp.Gateway.Persistence.Entities;
using SuperApp.Gateway.Proxy.Configuration;

namespace SuperApp.Gateway.Tests;

/// <summary>Mapping of the route configuration rows to YARP (ADR-0022): destination addresses and transform kinds the gateway does not accept.</summary>
public sealed class ProxyConfigMapperTests
{
    [Theory]
    [InlineData("http://knowledge-api.knowledge.svc.cluster.local:8080")]
    [InlineData("http://knowledge-api.knowledge.svc.cluster.local:8080/")]
    [InlineData("http://a.b.svc.cluster.local:1")]
    [InlineData("http://a1.b-2.svc.cluster.local:65535")]
    public void In_cluster_service_address_is_accepted(string address)
    {
        var mapping = ProxyConfigMapper.Map(Snapshot(address));

        Assert.True(mapping.IsValid);
        Assert.Equal(address, Assert.Single(Assert.Single(mapping.Clusters).Destinations!).Value.Address);
    }

    [Theory]
    [InlineData("http://evil.com/x.svc.cluster.local")]
    [InlineData("http://a.svc.cluster.local.evil.com")]
    [InlineData("http://a.b.svc.cluster.local.evil.com:80")]
    [InlineData("https://knowledge-api.knowledge.svc.cluster.local:8080")]
    [InlineData("http://knowledge-api.knowledge.svc.cluster.local")]
    [InlineData("http://knowledge-api.svc.cluster.local:8080")]
    [InlineData("http://x.knowledge-api.knowledge.svc.cluster.local:8080")]
    [InlineData("http://user@knowledge-api.knowledge.svc.cluster.local:8080")]
    [InlineData("http://knowledge-api.knowledge.svc.cluster.local:8080/path")]
    [InlineData("http://knowledge-api.knowledge.svc.cluster.local:8080/?q=1")]
    [InlineData("http://knowledge-api.knowledge.svc.cluster.local:8080/#f")]
    [InlineData("http://knowledge-api.knowledge.svc.cluster.local:8080//")]
    [InlineData("http://Knowledge-api.knowledge.svc.cluster.local:8080")]
    [InlineData("http://knowledge-.knowledge.svc.cluster.local:8080")]
    [InlineData("http://-knowledge.knowledge.svc.cluster.local:8080")]
    [InlineData("http://knowledge-api.knowledge.svc.cluster.local:0")]
    [InlineData("http://knowledge-api.knowledge.svc.cluster.local:08080")]
    [InlineData("http://knowledge-api.knowledge.svc.cluster.local:65536")]
    [InlineData("http://knowledge-api.knowledge.svc.cluster.local:8080\n")]
    public void Address_outside_cluster_makes_configuration_invalid(string address)
    {
        var mapping = ProxyConfigMapper.Map(Snapshot(address));

        Assert.False(mapping.IsValid);
        Assert.Contains("destination 'api'", Assert.Single(mapping.Errors), StringComparison.Ordinal);
        Assert.Empty(Assert.Single(mapping.Clusters).Destinations!);
    }

    [Fact]
    public void Unsupported_transform_kind_makes_configuration_invalid_instead_of_throwing()
    {
        var snapshot = Snapshot("http://knowledge-api.knowledge.svc.cluster.local:8080") with
        {
            Transforms =
            [
                new ProxyRouteTransform { Profile = "gateway-mobile", RouteId = "knowledge-api", Order = 1, Kind = "PathRemovePrefix", Value = "/api/knowledge" },
                new ProxyRouteTransform { Profile = "gateway-mobile", RouteId = "knowledge-api", Order = 2, Kind = "QueryValueSet", Name = "a", Value = "b" },
            ],
        };

        var mapping = ProxyConfigMapper.Map(snapshot);

        Assert.False(mapping.IsValid);
        Assert.Contains("'QueryValueSet'", Assert.Single(mapping.Errors), StringComparison.Ordinal);
        Assert.Single(Assert.Single(mapping.Routes).Transforms!);
    }

    [Fact]
    public void Transform_without_required_field_makes_configuration_invalid()
    {
        var snapshot = Snapshot("http://knowledge-api.knowledge.svc.cluster.local:8080") with
        {
            Transforms = [new ProxyRouteTransform { Profile = "gateway-mobile", RouteId = "knowledge-api", Order = 1, Kind = "RequestHeaderSet", Name = "X-A" }],
        };

        Assert.False(ProxyConfigMapper.Map(snapshot).IsValid);
    }

    private static ProxyConfigSnapshot Snapshot(string address) => new(
        "20260101000000_Test",
        [new ProxyCluster { Profile = "gateway-mobile", ClusterId = "knowledge", ActivityTimeoutSeconds = 30, HttpVersion = "1.1" }],
        [new ProxyDestination { Profile = "gateway-mobile", ClusterId = "knowledge", DestinationId = "api", Address = address }],
        [new ProxyRoute { Profile = "gateway-mobile", RouteId = "knowledge-api", ClusterId = "knowledge", Order = 1, Path = "/api/knowledge/{**rest}", AuthorizationPolicy = "anonymous" }],
        [],
        [],
        []);
}
