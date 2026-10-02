using SuperApp.Framework.Domain.Events;

namespace SuperApp.Framework.Application.Events;

/// <summary>
/// Reacts to a domain event raised by an aggregate, inside the same database transaction as the change that raised it.
/// </summary>
/// <remarks>
/// <para>
/// Handlers run while the unit of work saves a command's changes (<c>IUnitOfWork.SaveChangesAsync</c>), before anything is written.
/// Whatever they do is therefore atomic with the aggregate change: if a handler throws, the whole command is rolled back (ADR-0027).
/// </para>
/// <para>Typical uses:</para>
/// <list type="bullet">
///   <item><description>Translate the domain event into an integration event from <c>{Service}.Contracts</c> and pass it to
///   <see cref="IIntegrationEventPublisher"/>; the outbox guarantees it is sent if and only if the transaction commits.</description></item>
///   <item><description>Invalidate cached read models affected by the change (by cache tag).</description></item>
/// </list>
/// <para>Rules:</para>
/// <list type="bullet">
///   <item><description>Do not load and modify other aggregates here and do not call <c>SaveChangesAsync</c>. Cross-aggregate reactions are
///   eventually consistent: publish an integration event and let a consumer send a command.</description></item>
///   <item><description>Keep handlers fast and free of external calls (HTTP, e-mail); they hold the database transaction open.</description></item>
///   <item><description>Several handlers may exist for one event; do not rely on their order.</description></item>
///   <item><description>Declare them <c>internal sealed</c>, usually in <c>Application/IntegrationEvents</c> (translators) or in Infrastructure
///   (cache invalidation). <c>AddAppApplication</c> registers every implementation found in the scanned assemblies.</description></item>
/// </list>
/// </remarks>
/// <example>
/// Translating a domain event into an integration event:
/// <code>
/// internal sealed class MaterialPublishedTranslator(IIntegrationEventPublisher publisher)
///     : IDomainEventHandler&lt;MaterialPublished&gt;
/// {
///     public Task HandleAsync(MaterialPublished domainEvent, CancellationToken cancellationToken) =&gt;
///         publisher.PublishAsync(
///             new MaterialPublishedV1(domainEvent.MaterialId.Value, domainEvent.Type.ToString(), domainEvent.Title, domainEvent.PublishedAt),
///             cancellationToken);
/// }
/// </code>
/// </example>
/// <typeparam name="TEvent">The domain event type this handler reacts to.</typeparam>
/// <seealso cref="IDomainEvent"/>
/// <seealso cref="IDomainEventDispatcher"/>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>Handles one occurrence of the event.</summary>
    /// <param name="domainEvent">The event raised by the aggregate, carrying the data describing the change.</param>
    /// <param name="cancellationToken">Cancellation of the surrounding request or message.</param>
    /// <returns>A task that completes when the event is handled. A faulted task aborts the save and rolls back the command.</returns>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
