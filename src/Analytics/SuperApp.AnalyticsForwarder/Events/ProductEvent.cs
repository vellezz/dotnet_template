namespace SuperApp.AnalyticsForwarder.Events;

/// <summary>
/// A backend product analytics event, built by a consumer from an integration event and handed to an <see cref="IProductEventSink"/>
/// (ADR-0036).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Properties"/> is an allow-list: a consumer copies only identifiers and categories that analytics needs (material type, identifiers
/// of catalogue items), never free text, personal data or health data. For example <c>SleepEntryRecordedV1</c> carries sleep duration and
/// quality; the event built from it carries neither.
/// </para>
/// <para>
/// <see cref="Subject"/> is the CIAM subject of the user the event belongs to, or <see langword="null"/> for system events (a material was
/// published). It never leaves the process: the PostHog sink turns it into the pseudonymous analytics identifier, the logging sink does not
/// log it.
/// </para>
/// </remarks>
/// <param name="Name">Event name from <see cref="ProductEventNames"/>, in snake case with the service prefix.</param>
/// <param name="OccurredAt">When the business fact happened, taken from the integration event (not the time it was forwarded).</param>
/// <param name="MessageId">MassTransit message identifier of the integration event; sent as <c>message_id</c> to trace an event back to the message.</param>
/// <param name="Subject">CIAM subject of the user the event belongs to, or <see langword="null"/> for a system event.</param>
/// <param name="Properties">Allowed event properties (snake case keys, primitive values); see the remarks.</param>
public sealed record ProductEvent(
    string Name,
    DateTimeOffset OccurredAt,
    Guid? MessageId,
    string? Subject,
    IReadOnlyDictionary<string, object> Properties);
