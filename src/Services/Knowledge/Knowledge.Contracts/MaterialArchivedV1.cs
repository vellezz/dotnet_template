namespace Knowledge.Contracts;

/// <summary>
/// Integration event (published language of the Knowledge context, version 1): an educational material (article, video or podcast)
/// was archived and is no longer available to readers.
/// </summary>
/// <remarks>
/// <para>
/// <b>When it is published:</b> only on the transition to <c>Archived</c>, from <c>Draft</c> or from <c>Published</c>
/// (the <c>POST /v1/materials/{id}/archive</c> endpoint). Archiving is irreversible; archiving an already archived material succeeds
/// without publishing the event again. Note that a material archived while still a draft was never announced with
/// <see cref="MaterialPublishedV1"/>, so consumers may receive this event for a material they have never seen.
/// </para>
/// <para>
/// <b>Delivery:</b> written to the transactional outbox in the same database transaction as the state change and delivered to RabbitMQ
/// by <c>Knowledge.Worker</c>: never published for a rolled-back change, delivered <b>at least once</b>, possibly with a delay.
/// Consumers must be idempotent. The Knowledge service consumes this event itself to remove the material from all users' favorites.
/// </para>
/// <para>
/// <b>Versioning:</b> only backward-compatible changes are made to this type; a breaking change is published as a new type
/// (<c>MaterialArchivedV2</c>).
/// </para>
/// </remarks>
/// <param name="MaterialId">Identifier of the archived material (a GUID assigned by the Knowledge service, never <see cref="Guid.Empty"/>).</param>
/// <param name="ArchivedAt">
/// Moment of archiving in UTC, exactly as recorded by the material when it was archived (the same value as its last change time).
/// </param>
/// <seealso cref="CollectionArchivedV1"/>
public sealed record MaterialArchivedV1(Guid MaterialId, DateTimeOffset ArchivedAt);
