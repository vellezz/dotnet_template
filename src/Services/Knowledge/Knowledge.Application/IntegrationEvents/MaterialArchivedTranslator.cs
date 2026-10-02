using SuperApp.Framework.Application.Events;
using Knowledge.Domain.Materials.Events;
using Knowledge.Contracts;

namespace Knowledge.Application.IntegrationEvents;

/// <summary>
/// Translates the <see cref="MaterialArchived"/> domain event into the public <see cref="MaterialArchivedV1"/> integration event.
/// </summary>
/// <remarks>
/// <para>
/// Runs inside <c>IUnitOfWork.SaveChangesAsync</c> (ADR-0027) and writes the message to the outbox in the same transaction as the
/// archiving, so the message exists if and only if the archiving is committed. <c>ArchivedAt</c> is
/// <see cref="MaterialArchived.ArchivedAt"/>, the time the aggregate recorded when it was archived; the translator never reads a clock,
/// so the message and the stored state always agree.
/// </para>
/// <para>
/// Besides other contexts, the Knowledge Worker itself consumes this event (<c>MaterialArchivedConsumer</c>) and sends
/// <c>RemoveFavoritesOfItem</c> to delete all favorites of the archived material (ADR-0028).
/// </para>
/// </remarks>
/// <seealso cref="MaterialPublishedTranslator"/>
/// <seealso cref="CollectionArchivedTranslator"/>
/// <param name="publisher">Outbox-backed publisher of integration events.</param>
internal sealed class MaterialArchivedTranslator(IIntegrationEventPublisher publisher) : IDomainEventHandler<MaterialArchived>
{
    /// <inheritdoc />
    public Task HandleAsync(MaterialArchived domainEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new MaterialArchivedV1(domainEvent.MaterialId.Value, domainEvent.ArchivedAt), cancellationToken);
}
