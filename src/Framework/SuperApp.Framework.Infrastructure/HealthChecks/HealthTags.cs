namespace SuperApp.Framework.Infrastructure.HealthChecks;

/// <summary>
/// Tags that assign health checks to the Kubernetes probes and to the monitoring endpoint (ADR-0018).
/// </summary>
/// <remarks>
/// <para>The probe design deliberately keeps dependencies out of readiness and liveness:</para>
/// <list type="bullet">
///   <item><description><b>Startup</b> (<c>/health/startup</c>): the database is reachable and has no pending migrations. The pod receives traffic only after
///   it passes, which prevents running new code against an old schema.</description></item>
///   <item><description><b>Ready</b> (<c>/health/ready</c>): only the state of the pod itself (not shutting down). A database outage must not remove all pods
///   from the load balancer at once.</description></item>
///   <item><description><b>Live</b> (<c>/health/live</c>): no checks at all; the process answers HTTP.</description></item>
///   <item><description><b>Dependencies</b> (<c>/health/dependencies</c>): MSSQL, Redis and RabbitMQ, for dashboards and alerts only.</description></item>
/// </list>
/// <para>When adding a health check, tag it with exactly one of these constants.</para>
/// </remarks>
public static class HealthTags
{
    /// <summary>Checks evaluated by the Kubernetes startup probe: database reachable and no pending migrations.</summary>
    public const string Startup = "superapp-startup";

    /// <summary>Checks evaluated by the readiness probe: only the state of the pod, never external dependencies.</summary>
    public const string Ready = "superapp-ready";

    /// <summary>Checks of external dependencies, exposed for monitoring only and not used by any Kubernetes probe.</summary>
    public const string Dependencies = "superapp-dependencies";

    /// <summary>Tag MassTransit gives to its own bus health checks; included in the dependencies endpoint.</summary>
    public const string MassTransit = "masstransit";
}
