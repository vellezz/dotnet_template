using SuperApp.Framework.Domain.Events;
using Knowledge.Domain.Common;

namespace Knowledge.Domain.Materials.Events;

/// <summary>
/// Domain event: a draft material has been published and is now visible to readers. Raised by <see cref="Material.Publish"/> only on the
/// transition from <see cref="PublicationStatus.Draft"/>; publishing an already published material raises nothing.
/// </summary>
/// <remarks>
/// <c>MaterialPublishedTranslator</c> turns it into the integration event <c>MaterialPublishedV1</c> (type as its enum name, title,
/// <see cref="PublishedAt"/>)
/// published through the outbox in the same transaction (ADR-0027), so other contexts can react to new materials.
/// </remarks>
/// <param name="MaterialId">Identifier of the published material.</param>
/// <param name="Type">Kind of the material (article, video, podcast).</param>
/// <param name="Title">Title of the material at the moment of publication; later renames do not raise this event again.</param>
/// <param name="PublishedAt">
/// Moment of publication, identical to <see cref="Material.PublishedAt"/> of the aggregate (the <c>now</c> passed to
/// <see cref="Material.Publish"/>), so the integration event and the stored state never disagree.
/// </param>
public sealed record MaterialPublished(MaterialId MaterialId, MaterialType Type, string Title, DateTimeOffset PublishedAt) : IDomainEvent;
