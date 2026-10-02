using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SuperApp.Framework.Infrastructure.HealthChecks;

/// <summary>
/// Startup health check that fails while the database schema of <typeparamref name="TContext"/> is behind the code, i.e. there are migrations
/// in the assembly that are not recorded in the service's <c>__EFMigrationsHistory</c> table (ADR-0004, ADR-0018).
/// </summary>
/// <remarks>
/// Migrations are applied before the rollout by the <c>superapp-migrator</c> job (dev/test) or by the DBA (production), never by the service.
/// This check only verifies the result, so a pod never serves traffic against an outdated schema. Registered by <c>AddAppServiceDefaults</c>.
/// </remarks>
/// <typeparam name="TContext">The write database context whose migrations are checked.</typeparam>
/// <param name="context">The context instance resolved in the health check scope.</param>
public sealed class MigrationsAppliedHealthCheck<TContext>(TContext context) : IHealthCheck
    where TContext : DbContext
{
    /// <summary>Compares the migrations in the assembly with those applied to the database.</summary>
    /// <param name="healthCheckContext">Context supplied by the health check service.</param>
    /// <param name="cancellationToken">Cancellation of the probe request.</param>
    /// <returns>
    /// <see cref="HealthStatus.Healthy"/> when all migrations are applied; otherwise <see cref="HealthStatus.Unhealthy"/> listing the pending ones.
    /// An unreachable database throws, which the health check service reports as unhealthy.
    /// </returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthCheckContext, CancellationToken cancellationToken = default)
    {
        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        return pending.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Oczekujące migracje: {string.Join(", ", pending)}");
    }
}
