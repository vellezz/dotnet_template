using SuperApp.Framework.Infrastructure.Analytics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog;

namespace SuperApp.AnalyticsForwarder.Events;

/// <summary>
/// Periodically refreshes feature flag definitions from PostHog Cloud EU into <see cref="FeatureFlagsCache"/>
/// so that domain microservices can read them via HybridCache without direct external network egress (ADR-0036).
/// </summary>
internal sealed partial class FeatureFlagsRefreshWorker : BackgroundService
{
    private readonly IPostHogClient _client;
    private readonly FeatureFlagsCache _cache;
    private readonly IOptions<AnalyticsOptions> _options;
    private readonly ILogger<FeatureFlagsRefreshWorker> _logger;

    public FeatureFlagsRefreshWorker(
        IPostHogClient client,
        FeatureFlagsCache cache,
        IOptions<AnalyticsOptions> options,
        ILogger<FeatureFlagsRefreshWorker> logger)
    {
        _client = client;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _options.Value.FlagsRefreshInterval;
        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromSeconds(30);
        }

        // Initial fetch on startup before periodic ticks
        await RefreshAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Value.FeatureFlagsTimeout);

            var evaluations = await _client.EvaluateFlagsAsync(
                AnalyticsIdentity.System,
                new AllFeatureFlagsOptions { DisableGeoIp = true },
                timeout.Token);

            if (evaluations is not null)
            {
                var dictionary = evaluations.Keys.ToDictionary(
                    key => key,
                    key => evaluations.IsEnabled(key),
                    StringComparer.OrdinalIgnoreCase);

                _cache.UpdateSystemFlags(dictionary);
                LogFlagsRefreshed(_logger, dictionary.Count);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFlagsRefreshFailed(_logger, exception);
        }
    }

    [LoggerMessage(9005, LogLevel.Information, "Feature flags refreshed from PostHog ({Count} flags)")]
    private static partial void LogFlagsRefreshed(ILogger logger, int count);

    [LoggerMessage(9006, LogLevel.Warning, "Failed to refresh feature flags from PostHog; continuing with cached snapshot")]
    private static partial void LogFlagsRefreshFailed(ILogger logger, Exception exception);
}
