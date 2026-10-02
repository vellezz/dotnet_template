using PostHog;

namespace SuperApp.AnalyticsForwarder.Events;

/// <summary>Sends the events still queued in the PostHog client when the process stops (SIGTERM during a rollout or scale-down).</summary>
/// <remarks>
/// Hosted services stop in reverse registration order, after MassTransit has stopped consuming, so no new events arrive while flushing. The
/// flush is bounded by the host shutdown timeout; events that do not make it in time are lost (best effort, ADR-0036).
/// </remarks>
/// <param name="client">PostHog client registered by <c>AddAppAnalytics</c>.</param>
internal sealed class PostHogFlushOnShutdown(IPostHogClient client) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => client.FlushAsync().WaitAsync(cancellationToken);
}
