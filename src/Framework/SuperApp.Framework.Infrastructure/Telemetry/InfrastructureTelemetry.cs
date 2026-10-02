using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SuperApp.Framework.Infrastructure.Telemetry;

/// <summary>
/// OpenTelemetry sources of the framework infrastructure: spans of domain event dispatch, cache and feature flag metrics (ADR-0008, ADR-0020, ADR-0036).
/// </summary>
/// <remarks>
/// Registered with the tracer and meter providers by <c>AddAppServiceDefaults</c>. Service code creates its own sources; it does not add
/// instruments here.
/// </remarks>
public static class InfrastructureTelemetry
{
    /// <summary>Name of both the activity source and the meter (<c>SuperApp.Infrastructure</c>).</summary>
    public const string SourceName = "SuperApp.Infrastructure";

    /// <summary>Activity source of infrastructure spans, e.g. <c>domain-event MaterialPublished</c> around each domain event dispatch.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    /// <summary>Meter owning the infrastructure metrics below.</summary>
    public static readonly Meter Meter = new(SourceName);

    /// <summary>
    /// Counter <c>superapp.cache.requests</c> of <c>FailSafeCache</c> reads, tagged <c>result</c>: <c>fresh</c> (served from cache),
    /// <c>stale</c> (served while being refreshed) or <c>miss</c> (loaded from the source). Use it to judge the cache hit ratio.
    /// </summary>
    public static readonly Counter<long> CacheRequests =
        Meter.CreateCounter<long>("superapp.cache.requests", description: "Cache reads by result (ADR-0020)");

    /// <summary>
    /// Counter <c>superapp.cache.fail_safe.activations</c>: how many times a stale value was returned because refreshing failed with a transient error.
    /// A growing value means the data source is failing while users still get answers; alert on it.
    /// </summary>
    public static readonly Counter<long> CacheFailSafeActivations =
        Meter.CreateCounter<long>("superapp.cache.fail_safe.activations", description: "Stale cache values returned after a failed refresh");

    /// <summary>
    /// Counter <c>superapp.feature_flags.fallbacks</c>: how many feature flag evaluations used the code default instead of PostHog's answer, tagged
    /// <c>reason</c>: <c>timeout</c>, <c>unavailable</c> or <c>unknown flag</c> (ADR-0036). A steady value means users get default behaviour;
    /// check connectivity to PostHog and that the flag exists in the PostHog project.
    /// </summary>
    public static readonly Counter<long> FeatureFlagFallbacks =
        Meter.CreateCounter<long>("superapp.feature_flags.fallbacks", description: "Feature flag evaluations that used the code default");
}
