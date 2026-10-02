using SuperApp.Framework.Application.FeatureFlags;
using Microsoft.Extensions.Configuration;

namespace SuperApp.Framework.Infrastructure.Analytics;

/// <summary>
/// <see cref="IFeatureFlags"/> used when analytics is disabled: reads flags from configuration (<c>FeatureFlags:{key}</c>) and falls back to
/// <see cref="FeatureFlag.DefaultValue"/> (ADR-0036).
/// </summary>
/// <remarks>
/// Registered by <c>AddAppFeatureFlags</c> when <see cref="AnalyticsOptions.ProjectToken"/> is not set, which is the case locally and in tests.
/// To try a feature locally, set the flag in <c>appsettings.Development.json</c>, <c>launchSettings.json</c> or an environment variable
/// (<c>FeatureFlags__knowledge_material_ratings=true</c>). The value is the same for every user; percentage rollouts exist only in PostHog.
/// Configuration is read on every call, so a reloaded <c>appsettings</c> file takes effect without a restart.
/// </remarks>
/// <param name="configuration">Application configuration with the optional <c>FeatureFlags</c> section.</param>
internal sealed class ConfigurationFeatureFlags(IConfiguration configuration) : IFeatureFlags
{
    /// <summary>Name of the configuration section with flag values (<c>FeatureFlags</c>).</summary>
    public const string SectionName = "FeatureFlags";

    /// <inheritdoc />
    public ValueTask<bool> IsEnabledAsync(FeatureFlag flag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(configuration.GetValue<bool?>($"{SectionName}:{flag.Key}") ?? flag.DefaultValue);
    }
}
