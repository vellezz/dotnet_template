using System.Net.Http.Json;
using SuperApp.Framework.Application.FeatureFlags;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Infrastructure.Telemetry;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SuperApp.Framework.Infrastructure.Analytics;

/// <summary>
/// <see cref="IFeatureFlags"/> backed by <see cref="HybridCache"/> (L1 memory + L2 Redis, ADR-0020) and the internal
/// <c>analytics-forwarder</c> service, eliminating direct external calls and egress from domain microservices.
/// </summary>
internal sealed partial class HybridCacheFeatureFlags(
    HybridCache cache,
    IHttpClientFactory httpClientFactory,
    ICurrentUser currentUser,
    AnalyticsIdentity identity,
    IOptions<AnalyticsOptions> options,
    ILogger<HybridCacheFeatureFlags> logger) : IFeatureFlags
{
    private const string ForwarderClientName = "AnalyticsForwarder";

    /// <inheritdoc />
    public async ValueTask<bool> IsEnabledAsync(FeatureFlag flag, CancellationToken cancellationToken = default)
    {
        var distinctId = identity.ForSubject(currentUser.Subject) ?? AnalyticsIdentity.System;
        var cacheKey = $"superapp:featureflags:{distinctId}";

        IReadOnlyDictionary<string, bool>? evaluations = null;

        try
        {
            evaluations = await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await FetchFlagsAsync(distinctId, cancel),
                new HybridCacheEntryOptions
                {
                    LocalCacheExpiration = TimeSpan.FromSeconds(30),
                    Expiration = TimeSpan.FromMinutes(5),
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            InfrastructureTelemetry.FeatureFlagFallbacks.Add(1, new KeyValuePair<string, object?>("reason", "cache_error"));
            LogEvaluationFailed(logger, exception);
        }

        if (evaluations is null || !evaluations.TryGetValue(flag.Key, out var isEnabled))
        {
            Fallback(flag, "unknown flag or fallback", exception: null);
            return flag.DefaultValue;
        }

        return isEnabled;
    }

    private async Task<IReadOnlyDictionary<string, bool>> FetchFlagsAsync(string distinctId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.FeatureFlagsTimeout);

        try
        {
            var client = httpClientFactory.CreateClient(ForwarderClientName);
            var uri = $"/internal/flags?distinctId={Uri.EscapeDataString(distinctId)}";
            var result = await client.GetFromJsonAsync<Dictionary<string, bool>>(uri, timeout.Token);
            return result ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            InfrastructureTelemetry.FeatureFlagFallbacks.Add(1, new KeyValuePair<string, object?>("reason", exception is OperationCanceledException ? "timeout" : "unavailable"));
            LogEvaluationFailed(logger, exception);
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
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
