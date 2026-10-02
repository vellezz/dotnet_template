using SuperApp.Framework.Infrastructure.Analytics;
using PostHog;

namespace SuperApp.AnalyticsForwarder.Events;

/// <summary>Sends backend product events to PostHog Cloud EU through the PostHog .NET client (ADR-0036).</summary>
/// <remarks>
/// <para>
/// The distinct identifier is the pseudonymous analytics identifier of <see cref="ProductEvent.Subject"/> (<see cref="AnalyticsIdentity"/>), the
/// same one the web and mobile clients use for the person, or <see cref="AnalyticsIdentity.System"/> for system events, which are also marked
/// with <c>$process_person_profile = false</c> so that PostHog does not create a person for them. Every event carries <c>source = backend</c>
/// and <c>message_id</c>; its timestamp is the time of the business fact.
/// </para>
/// <para>
/// The client queues events in memory and sends them in batches in the background, retrying on network errors; events still queued are
/// flushed when the process stops (<see cref="PostHogFlushOnShutdown"/>). Delivery is therefore best effort: analytics data may lose events in
/// rare failures, which is acceptable for analytics and is why business logic never depends on it.
/// </para>
/// </remarks>
/// <param name="client">PostHog client registered by <c>AddAppAnalytics</c>.</param>
/// <param name="identity">Maps the CIAM subject to the analytics identifier.</param>
/// <param name="logger">Logger for events the client refused to queue.</param>
internal sealed partial class PostHogProductEventSink(IPostHogClient client, AnalyticsIdentity identity, ILogger<PostHogProductEventSink> logger)
    : IProductEventSink
{
    /// <inheritdoc />
    public void Capture(ProductEvent productEvent)
    {
        var properties = new Dictionary<string, object>(productEvent.Properties, StringComparer.Ordinal)
        {
            ["source"] = "backend",
        };
        if (productEvent.MessageId is { } messageId)
        {
            properties["message_id"] = messageId.ToString();
        }

        var distinctId = identity.ForSubject(productEvent.Subject);
        if (distinctId is null)
        {
            distinctId = AnalyticsIdentity.System;
            properties["$process_person_profile"] = false;
        }

        if (!client.Capture(distinctId, productEvent.Name, properties, groups: null, flags: null, timestamp: productEvent.OccurredAt))
        {
            LogNotQueued(logger, productEvent.Name);
        }
    }

    [LoggerMessage(9002, LogLevel.Warning, "Product event {EventName} was not queued for PostHog (queue full or client disposed)")]
    private static partial void LogNotQueued(ILogger logger, string eventName);
}
