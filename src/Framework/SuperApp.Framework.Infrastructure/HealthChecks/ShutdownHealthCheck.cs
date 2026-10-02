using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace SuperApp.Framework.Infrastructure.HealthChecks;

/// <summary>
/// Readiness check that becomes unhealthy as soon as the host starts stopping (SIGTERM), so Kubernetes removes the pod from the service endpoints
/// while in-flight requests finish (graceful shutdown, ADR-0018).
/// </summary>
internal sealed class ShutdownHealthCheck(IHostApplicationLifetime lifetime) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(lifetime.ApplicationStopping.IsCancellationRequested
            ? HealthCheckResult.Unhealthy("Zamykanie procesu")
            : HealthCheckResult.Healthy());
}
