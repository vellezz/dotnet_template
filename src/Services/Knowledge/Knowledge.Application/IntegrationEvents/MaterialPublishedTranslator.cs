using SuperApp.Framework.Application.Events;
using Knowledge.Domain.Materials.Events;
using Knowledge.Contracts;

namespace Knowledge.Application.IntegrationEvents;

/// <summary>
/// Translates the <see cref="MaterialPublished"/> domain event into the public <see cref="MaterialPublishedV1"/> integration event,
/// so that other bounded contexts learn that a new material is available.
/// </summary>
/// <remarks>
/// <para>
/// Domain events are internal and may change freely; integration events in <c>Knowledge.Contracts</c> are a versioned public contract
/// that carries only what other contexts need. Translators in this folder are the only bridge between the two.
/// </para>
/// <para>
/// The handler runs inside <c>IUnitOfWork.SaveChangesAsync</c> (ADR-0027), in the same transaction as the aggregate change, and
/// <see cref="IIntegrationEventPublisher"/> writes the message to the outbox. The message is therefore sent if and only if the
/// publication is committed. It is raised only on the first publication of a material.
/// </para>
/// <para>
/// The material type is sent as its enum name (<c>Article</c>, <c>Video</c>, <c>Podcast</c>) and <c>PublishedAt</c> is
/// <see cref="MaterialPublished.PublishedAt"/>, which is exactly the <c>PublishedAt</c> stored on the aggregate; the translator never
/// reads a clock.
/// </para>
/// </remarks>
/// <seealso cref="MaterialArchivedTranslator"/>
/// <seealso cref="CollectionArchivedTranslator"/>
/// <param name="publisher">Outbox-backed publisher of integration events.</param>
internal sealed class MaterialPublishedTranslator(IIntegrationEventPublisher publisher) : IDomainEventHandler<MaterialPublished>
{
    /// <inheritdoc />
    public Task HandleAsync(MaterialPublished domainEvent, CancellationToken cancellationToken) =>
        publisher.PublishAsync(
            new MaterialPublishedV1(domainEvent.MaterialId.Value, domainEvent.Type.ToString(), domainEvent.Title, domainEvent.PublishedAt),
            cancellationToken);
}
