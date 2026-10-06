namespace SuperApp.Framework.Infrastructure.Analytics;

/// <summary>
/// Settings of product analytics and feature flags, bound from the <c>Analytics</c> configuration section (ADR-0036).
/// </summary>
/// <remarks>
/// <para>
/// Analytics is enabled when <see cref="ProjectToken"/> is set. Then events and flags go to PostHog Cloud EU and <see cref="IdKey"/> is
/// required; both are validated at startup, so a half-configured process does not start. When it is not set (local development, tests,
/// any environment without analytics) nothing is sent anywhere, flags are read from configuration (<c>FeatureFlags:{key}</c>) and
/// <see cref="AnalyticsIdentity"/> returns no identifiers.
/// </para>
/// <para>
/// Secrets: <see cref="IdKey"/> and <see cref="FeatureFlagsKey"/> come from Vault (External Secrets), never from <c>appsettings*.json</c>.
/// <see cref="ProjectToken"/> is not secret (web and mobile clients embed it), but it is set per environment, so it also comes from the
/// deployment configuration. Every process that sees users (gateway, services, analytics forwarder) must use the same <see cref="IdKey"/>,
/// otherwise the same person gets different identifiers in different places.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // environment variables of a deployment (values from Vault)
/// Analytics__ProjectToken=phc_...
/// Analytics__IdKey=...               // at least 32 characters, the same for all processes of the environment
/// Analytics__FeatureFlagsKey=phs_... // optional: enables local flag evaluation in the process
/// </code>
/// </example>
public sealed class AnalyticsOptions
{
    /// <summary>Name of the configuration section (<c>Analytics</c>).</summary>
    public const string SectionName = "Analytics";

    /// <summary>Minimum length of <see cref="IdKey"/>: 32 characters, so that identifiers cannot be reversed by trying keys.</summary>
    public const int MinimumIdKeyLength = 32;

    /// <summary>
    /// Gets or sets the PostHog project token (<c>phc_...</c>) of the environment, or <see langword="null"/> to disable analytics.
    /// </summary>
    public string? ProjectToken { get; set; }

    /// <summary>
    /// Gets or sets the PostHog ingestion host; default <c>https://eu.i.posthog.com</c> (PostHog Cloud EU). Must be HTTPS on a
    /// <c>posthog.com</c> host.
    /// </summary>
    public Uri Host { get; set; } = new("https://eu.i.posthog.com");

    /// <summary>
    /// Gets or sets the host of PostHog's static assets (the browser library and its extensions), proxied by the BFF under
    /// <c>/ingest/static</c>; default <c>https://eu-assets.i.posthog.com</c>.
    /// </summary>
    public Uri AssetsHost { get; set; } = new("https://eu-assets.i.posthog.com");

    /// <summary>
    /// Gets or sets the PostHog feature flags secure API key (<c>phs_...</c>), or <see langword="null"/>. With it, the process downloads flag
    /// definitions periodically and evaluates flags locally, without a network call per evaluation; without it, each evaluation asks PostHog.
    /// Secret: from Vault.
    /// </summary>
    public string? FeatureFlagsKey { get; set; }

    /// <summary>
    /// Gets or sets the secret key of the HMAC that turns a CIAM subject into the pseudonymous analytics identifier (<see cref="AnalyticsIdentity"/>).
    /// Required when analytics is enabled; at least <see cref="MinimumIdKeyLength"/> characters. Secret: from Vault. Changing it changes
    /// every user's identifier, which splits their history in PostHog.
    /// </summary>
    public string? IdKey { get; set; }

    /// <summary>
    /// Gets or sets how long a feature flag evaluation may take before <see cref="SuperApp.Framework.Application.FeatureFlags.FeatureFlag.DefaultValue"/>
    /// is used instead; default 1 second.
    /// </summary>
    public TimeSpan FeatureFlagsTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the base URL of the internal analytics forwarder service that provides feature flags over the cluster network;
    /// default <c>http://analytics-forwarder:8080</c>.
    /// </summary>
    public Uri ForwarderUrl { get; set; } = new("http://analytics-forwarder:8080");

    /// <summary>
    /// Gets or sets the interval at which the forwarder periodically refreshes feature flags from PostHog into the cache;
    /// default 30 seconds.
    /// </summary>
    public TimeSpan FlagsRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets a value indicating whether analytics is enabled, that is whether <see cref="ProjectToken"/> is set.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(ProjectToken);

    /// <summary>Checks the settings when analytics is enabled; used by options validation at startup.</summary>
    /// <returns><see langword="true"/> when analytics is disabled, or enabled with a valid key and PostHog hosts.</returns>
    internal bool IsValid() =>
        !Enabled
        || (IdKey is { Length: >= MinimumIdKeyLength }
            && IsPostHogHost(Host)
            && IsPostHogHost(AssetsHost)
            && FeatureFlagsTimeout > TimeSpan.Zero
            && FlagsRefreshInterval > TimeSpan.Zero);

    // The hosts are reached from inside the cluster and by the BFF proxy; only PostHog's own HTTPS hosts are accepted, so a configuration
    // mistake cannot turn the proxy into a gateway to an arbitrary site.
    private static bool IsPostHogHost(Uri host) =>
        host is { IsAbsoluteUri: true, Scheme: "https", AbsolutePath: "/", Query: "", UserInfo: "" }
        && host.Host.EndsWith(".posthog.com", StringComparison.OrdinalIgnoreCase);
}
