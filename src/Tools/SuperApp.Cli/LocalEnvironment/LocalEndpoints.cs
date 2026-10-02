using SuperApp.Cli.Repository;

namespace SuperApp.Cli.LocalEnvironment;

/// <summary>Addresses of the local environment of <c>deploy/local/docker-compose.yml</c> (ADR-0034), read from the published host ports.</summary>
/// <remarks>
/// <para>
/// Nothing is hard-coded: a component found in the compose file is reached on its first published host port, so a new service or BFF
/// added by <c>dotnet superapp add</c> is probed and tested without changes here. The gateway profiles publish HTTPS on their first port
/// (<c>bff-web</c> 5001, <c>gateway-mobile</c> 5002) with the local development certificate; everything else is plain HTTP.
/// </para>
/// </remarks>
/// <param name="model">The scanned repository.</param>
internal sealed class LocalEndpoints(RepositoryModel model)
{
    /// <summary>Name of the local realm (ADR-0031).</summary>
    public const string Realm = "superapp";

    /// <summary>Base address of the local Keycloak, e.g. <c>http://localhost:8081</c>; <see langword="null"/> without the service.</summary>
    public string? Keycloak => Http("keycloak");

    /// <summary>Base address of the web profile of the gateway, e.g. <c>https://localhost:5001</c>.</summary>
    public string? BffWeb => Https("bff-web");

    /// <summary>Base address of the mobile profile of the gateway, e.g. <c>https://localhost:5002</c>.</summary>
    public string? GatewayMobile => Https("gateway-mobile");

    /// <summary>BFFs of the experiences with their base addresses, e.g. <c>(example, http://localhost:5120)</c>.</summary>
    public IReadOnlyList<(string Experience, string Address)> Bffs =>
        [.. model.Bffs.Select(bff => (bff.Key, Http($"{bff.Key}-bff"))).Where(item => item.Item2 is not null).Select(item => (item.Key, item.Item2!))];

    /// <summary>Domain service APIs with their base addresses, e.g. <c>(knowledge, http://localhost:5101)</c>.</summary>
    public IReadOnlyList<(string Service, string Address)> Services =>
        [.. model.Services.Select(service => (service.Key, Http($"{service.Key}-api"))).Where(item => item.Item2 is not null).Select(item => (item.Key, item.Item2!))];

    /// <summary>Token endpoint of the local realm.</summary>
    public string? TokenEndpoint => Keycloak is { } keycloak ? $"{keycloak}/realms/{Realm}/protocol/openid-connect/token" : null;

    /// <summary>Health and readiness probes of every component: name and URL.</summary>
    /// <returns>The probes, the realm first.</returns>
    public IReadOnlyList<(string Name, string Url)> Probes()
    {
        var probes = new List<(string, string)>();
        if (Keycloak is { } keycloak)
        {
            probes.Add(("keycloak (realm)", $"{keycloak}/realms/{Realm}/.well-known/openid-configuration"));
        }

        foreach (var (name, address) in (IEnumerable<(string, string?)>)[("bff-web", BffWeb), ("gateway-mobile", GatewayMobile)])
        {
            if (address is not null)
            {
                probes.Add((name, $"{address}/health/live"));
            }
        }

        probes.AddRange(Bffs.Select(bff => ($"{bff.Experience}-bff", $"{bff.Address}/health/live")));
        probes.AddRange(Services.Select(service => ($"{service.Service}-api", $"{service.Address}/health/live")));
        return probes;
    }

    private string? Https(string service) => Port(service) is { } port ? $"https://localhost:{port}" : null;

    private string? Http(string service) => Port(service) is { } port ? $"http://localhost:{port}" : null;

    private int? Port(string service) => model.Compose.FirstOrDefault(candidate => candidate.Name == service)?.HostPorts.FirstOrDefault() is { } port and > 0 ? port : null;
}
