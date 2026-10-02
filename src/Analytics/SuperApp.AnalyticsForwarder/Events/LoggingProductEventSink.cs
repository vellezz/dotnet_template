namespace SuperApp.AnalyticsForwarder.Events;

/// <summary>
/// <see cref="IProductEventSink"/> used when analytics is disabled (locally, in tests): logs the event name and property names instead of
/// sending anything (ADR-0036).
/// </summary>
/// <remarks>
/// Lets a developer check locally which product event an action produces, without a PostHog project. Property values and the subject are
/// not logged: they could contain personal data, and the names are enough to verify the mapping.
/// </remarks>
/// <param name="logger">Logger of the forwarder.</param>
internal sealed partial class LoggingProductEventSink(ILogger<LoggingProductEventSink> logger) : IProductEventSink
{
    /// <inheritdoc />
    public void Capture(ProductEvent productEvent) =>
        LogNotSent(logger, productEvent.Name, productEvent.Subject is null ? "system" : "user", string.Join(",", productEvent.Properties.Keys));

    [LoggerMessage(9001, LogLevel.Information, "Analytics disabled: product event {EventName} ({EventOwner}, properties: {PropertyNames}) not sent")]
    private static partial void LogNotSent(ILogger logger, string eventName, string eventOwner, string propertyNames);
}
