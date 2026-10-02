using SuperApp.Framework.Application.FeatureFlags;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog;
using FeatureFlagEvaluations = PostHog.Features.FeatureFlagEvaluations;

namespace SuperApp.Framework.Infrastructure.Analytics;

/// <summary>
/// <see cref="IFeatureFlags"/> backed by PostHog: evaluates a flag for the current user's analytics identifier, with a timeout and a fallback to
/// <see cref="FeatureFlag.DefaultValue"/> (ADR-0036).
/// </summary>
/// <remarks>
/// <para>
/// Scoped: one instance per HTTP request or consumed message asks PostHog once for all flags of the caller and answers every later call
/// from that snapshot, so a handler (and the behaviors around it) see the same values for the whole request. The distinct identifier is <see cref="AnalyticsIdentity.ForSubject"/> of
/// <see cref="ICurrentUser.Subject"/>, or <see cref="AnalyticsIdentity.System"/> without a user; GeoIP is disabled, because the server's address
/// says nothing about the user.
/// </para>
/// <para>
/// With <see cref="AnalyticsOptions.FeatureFlagsKey"/> the PostHog client keeps flag definitions in memory and evaluates locally; otherwise each
/// first evaluation in a scope is a call to PostHog. Either way the call is bounded by <see cref="AnalyticsOptions.FeatureFlagsTimeout"/>; on
/// timeout, network error or any other failure of the provider every flag of the scope uses its default value (warning, event ID 401), an unknown flag uses its default value
/// (warning, event ID 400), and <c>superapp.feature_flags.fallbacks</c> grows. Cancellation requested by the caller is not a fallback: it propagates.
/// </para>
/// </remarks>
/// <param name="client">PostHog client registered by <c>AddAppAnalytics</c>.</param>
/// <param name="currentUser">The caller of the current request or message.</param>
/// <param name="identity">Maps the caller's subject to the analytics identifier.</param>
/// <param name="options">Analytics settings (evaluation timeout).</param>
/// <param name="logger">Logger for fallbacks.</param>
internal sealed partial class PostHogFeatureFlags(
    IPostHogClient client,
    ICurrentUser currentUser,
    AnalyticsIdentity identity,
    IOptions<AnalyticsOptions> options,
    ILogger<PostHogFeatureFlags> logger) : IFeatureFlags
{
    private Task<FeatureFlagEvaluations?>? _evaluations;

    /// <inheritdoc />
    public async ValueTask<bool> IsEnabledAsync(FeatureFlag flag, CancellationToken cancellationToken)
    {
        // One request to PostHog per scope evaluates all flags; later calls in the scope reuse the snapshot.
        _evaluations ??= EvaluateAsync(cancellationToken);
        var evaluations = await _evaluations;
        if (evaluations is null)
        {
            return flag.DefaultValue;
        }

        if (!evaluations.Keys.Contains(flag.Key))
        {
            Fallback(flag, "unknown flag", exception: null);
            return flag.DefaultValue;
        }

        return evaluations.IsEnabled(flag.Key);
    }

    private async Task<FeatureFlagEvaluations?> EvaluateAsync(CancellationToken cancellationToken)
    {
        var distinctId = identity.ForSubject(currentUser.Subject) ?? AnalyticsIdentity.System;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.FeatureFlagsTimeout);

        try
        {
            return await client.EvaluateFlagsAsync(distinctId, new AllFeatureFlagsOptions { DisableGeoIp = true }, timeout.Token);
        }
        // Any failure of the flag provider (timeout, network, an unexpected response the SDK does not handle) falls back to the defaults:
        // a flag must never fail the request. Only cancellation requested by the caller propagates.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            InfrastructureTelemetry.FeatureFlagFallbacks.Add(1, new KeyValuePair<string, object?>("reason", exception is OperationCanceledException ? "timeout" : "unavailable"));
            LogEvaluationFailed(logger, exception);
            return null;
        }
    }

    private void Fallback(FeatureFlag flag, string reason, Exception? exception)
    {
        InfrastructureTelemetry.FeatureFlagFallbacks.Add(1, new KeyValuePair<string, object?>("reason", reason));
        LogFallback(logger, flag.Key, reason, flag.DefaultValue, exception);
    }

    [LoggerMessage(401, LogLevel.Warning, "Feature flags could not be evaluated; every flag of this request uses its default value")]
    private static partial void LogEvaluationFailed(ILogger logger, Exception exception);

    [LoggerMessage(400, LogLevel.Warning, "Feature flag {FlagKey}: {Reason}, using the default value {DefaultValue}")]
    private static partial void LogFallback(ILogger logger, string flagKey, string reason, bool defaultValue, Exception? exception);
}
