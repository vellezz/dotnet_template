using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SuperApp.Framework.Infrastructure.HealthChecks;

/// <summary>
/// Reports whether the Redis L2 cache answers. Registered under the <c>superapp-dependencies</c> tag only, so an unavailable Redis
/// is visible in monitoring but never takes pods out of service (the cache degrades to memory only).
/// </summary>
internal sealed class DistributedCacheHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await cache.GetAsync("health:probe", cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
