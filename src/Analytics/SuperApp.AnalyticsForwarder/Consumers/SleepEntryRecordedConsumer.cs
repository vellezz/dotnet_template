using SuperApp.AnalyticsForwarder.Events;
using MassTransit;
using SleepDiary.Contracts;

namespace SuperApp.AnalyticsForwarder.Consumers;

/// <summary>
/// Forwards <see cref="SleepEntryRecordedV1"/> to product analytics as <see cref="ProductEventNames.SleepDiaryEntryRecorded"/> (ADR-0036).
/// </summary>
/// <remarks>
/// Event of the user who recorded the entry: the CIAM subject from the message becomes the pseudonymous analytics identifier in the sink.
/// The entry's date, sleep duration and quality are health data (GDPR art. 9) and are deliberately not forwarded; add properties only
/// after a privacy review (ADR-0036).
/// The consumer only maps the message; it holds no state and calls no service, so a redelivered message only repeats the event.
/// </remarks>
/// <param name="sink">Destination of product events.</param>
public sealed class SleepEntryRecordedConsumer(IProductEventSink sink) : IConsumer<SleepEntryRecordedV1>
{
    /// <summary>Maps the message to the product event and queues it.</summary>
    /// <param name="context">The consumed message and its MassTransit metadata (message identifier).</param>
    /// <returns>A completed task: queuing does not wait for PostHog.</returns>
    public Task Consume(ConsumeContext<SleepEntryRecordedV1> context)
    {
        var message = context.Message;
        sink.Capture(new ProductEvent(
            ProductEventNames.SleepDiaryEntryRecorded,
            message.RecordedAt,
            context.MessageId,
            message.UserId,
            new Dictionary<string, object>()));
        return Task.CompletedTask;
    }
}
