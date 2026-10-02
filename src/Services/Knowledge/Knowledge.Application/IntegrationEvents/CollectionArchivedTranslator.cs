using SuperApp.Framework.Application.Events;
using Knowledge.Domain.Collections.Events;
using Knowledge.Contracts;

namespace Knowledge.Application.IntegrationEvents;

/// <summary>
/// Translates the <see cref="CollectionArchived"/> domain event into the public <see cref="CollectionArchivedV1"/> integration event.
/// </summary>
/// <remarks>
/// <para>
/// Runs inside <c>IUnitOfWork.SaveChangesAsync</c> (ADR-0027) and writes the message to the outbox in the same transaction as the
/// archiving, so the message exists if and only if the archiving is committed. <c>ArchivedAt</c> is
/// <see cref="CollectionArchived.ArchivedAt"/>, the time the aggregate recorded when it was archived; the translator never reads a clock,
/// so the message and the stored state always agree.
/// </para>
/// <para>
/// The Knowledge Worker consumes this event (<c>CollectionArchivedConsumer</c>) and sends <c>RemoveFavoritesOfItem</c> to delete all
/// favorites of the archived collection.
/// </para>
/// </remarks>
/// <seealso cref="MaterialArchivedTranslator"/>
/// <param name="publisher">Outbox-backed publisher of integration events.</param>
internal sealed class CollectionArchivedTranslator(IIntegrationEventPublisher publisher) : IDomainEventHandler<CollectionArchived>
{
    /// <inheritdoc />
    public Task HandleAsync(CollectionArchived domainEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new CollectionArchivedV1(domainEvent.CollectionId.Value, domainEvent.ArchivedAt), cancellationToken);
}
