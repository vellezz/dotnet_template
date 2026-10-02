namespace SuperApp.Framework.Application.Events;

/// <summary>
/// Publishes integration events, the messages other services subscribe to, with the guarantee that a message is sent
/// if and only if the transaction that produced it commits (transactional outbox, ADR-0005).
/// </summary>
/// <remarks>
/// <para>
/// Integration events are the published language of a bounded context: <c>sealed record</c> types in the <c>{Service}.Contracts</c>
/// project, versioned by name (<c>MaterialPublishedV1</c>), containing only primitives and only the data other contexts need.
/// Changing them must stay backward compatible; a breaking change means a new type (<c>...V2</c>).
/// </para>
/// <para>How publishing works:</para>
/// <list type="number">
///   <item><description>An <see cref="IDomainEventHandler{TEvent}"/> calls <see cref="PublishAsync{TEvent}"/> while the unit of work is saving.</description></item>
///   <item><description>The message is stored in the outbox table of the service schema in the same transaction as the aggregate change.</description></item>
///   <item><description>After commit, the outbox delivery service in the Worker sends it to RabbitMQ; delivery is retried until it succeeds.</description></item>
/// </list>
/// <para>
/// Delivery is at least once: consumers must be idempotent. Do not publish from command handlers or controllers; always go through
/// a domain event so that the aggregate stays the source of truth for what happened.
/// </para>
/// </remarks>
/// <seealso cref="IDomainEventHandler{TEvent}"/>
public interface IIntegrationEventPublisher
{
    /// <summary>Adds an integration event to the outbox of the current unit of work.</summary>
    /// <typeparam name="TEvent">The integration event type from the service's <c>Contracts</c> project; its name determines the message type in RabbitMQ.</typeparam>
    /// <param name="integrationEvent">The event to publish.</param>
    /// <param name="cancellationToken">Cancellation of the surrounding request or message.</param>
    /// <returns>
    /// A task that completes when the message is added to the outbox. The message is not sent yet; it leaves the service only after the
    /// transaction commits, and is discarded if it rolls back.
    /// </returns>
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class;
}
