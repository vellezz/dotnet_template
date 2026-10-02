namespace SuperApp.Gateway.Proxy.Configuration;

/// <summary>
/// Startup probe check of the gateway (tag <c>startup</c>, ADR-0018, ADR-0022): healthy once <see cref="SuperApp.Gateway.Proxy.Configuration.DatabaseProxyConfigProvider"/>
/// has a validated route configuration, loaded from the database or, if the database was unavailable at startup, from the cache.
/// </summary>
/// <remarks>
/// A gateway without routes cannot forward any API call, so Kubernetes must not send traffic to it; the pod keeps failing the
/// startup probe instead. Once a configuration is loaded the check stays healthy, because later reload failures keep the active configuration.
/// </remarks>
/// <param name="provider">The route configuration provider.</param>
internal sealed class ProxyConfigLoadedHealthCheck(DatabaseProxyConfigProvider provider) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    /// <summary>Reports whether a route configuration is active.</summary>
    /// <param name="context">Health check context; not used.</param>
    /// <param name="cancellationToken">Not used; the check is synchronous.</param>
    /// <returns>Healthy with the active migration id in the description, or unhealthy when no configuration has been loaded yet.</returns>
    public Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(provider.IsLoaded
            ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy($"Konfiguracja {provider.ActiveMigrationId}")
            : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Brak konfiguracji tras"));
}
