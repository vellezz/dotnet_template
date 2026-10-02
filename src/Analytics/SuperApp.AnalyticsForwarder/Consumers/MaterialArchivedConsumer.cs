using SuperApp.AnalyticsForwarder.Events;
using MassTransit;
using Knowledge.Contracts;

namespace SuperApp.AnalyticsForwarder.Consumers;

/// <summary>
/// Forwards <see cref="MaterialArchivedV1"/> to product analytics as <see cref="ProductEventNames.KnowledgeMaterialArchived"/> (ADR-0036).
/// </summary>
/// <remarks>
/// System event.
/// The consumer only maps the message; it holds no state and calls no service, so a redelivered message only repeats the event.
/// </remarks>
/// <param name="sink">Destination of product events.</param>
public sealed class MaterialArchivedConsumer(IProductEventSink sink) : IConsumer<MaterialArchivedV1>
{
    /// <summary>Maps the message to the product event and queues it.</summary>
    /// <param name="context">The consumed message and its MassTransit metadata (message identifier).</param>
    /// <returns>A completed task: queuing does not wait for PostHog.</returns>
    public Task Consume(ConsumeContext<MaterialArchivedV1> context)
    {
        var message = context.Message;
        sink.Capture(new ProductEvent(
            ProductEventNames.KnowledgeMaterialArchived,
            message.ArchivedAt,
            context.MessageId,
            Subject: null,
            new Dictionary<string, object>
            {
                ["material_id"] = message.MaterialId.ToString(),
            }));
        return Task.CompletedTask;
    }
}
