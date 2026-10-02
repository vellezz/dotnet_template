namespace SuperApp.AnalyticsForwarder.Events;

/// <summary>Destination of backend product analytics events: PostHog when analytics is enabled, the log otherwise (ADR-0036).</summary>
/// <remarks>
/// Consumers depend on this interface only, so they are tested without PostHog and run unchanged locally, where analytics is disabled.
/// Implementations queue the event and return immediately; sending happens in the background, in batches.
/// </remarks>
public interface IProductEventSink
{
    /// <summary>Queues <paramref name="productEvent"/> for sending.</summary>
    /// <param name="productEvent">The event with allow-listed properties only.</param>
    void Capture(ProductEvent productEvent);
}
