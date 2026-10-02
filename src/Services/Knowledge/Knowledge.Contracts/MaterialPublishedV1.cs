namespace Knowledge.Contracts;

// Published language of the Knowledge service (ADR-0005): primitive types only; a breaking change means a new version of the type.

/// <summary>
/// Integration event (published language of the Knowledge context, version 1): an educational material was published
/// and is visible to readers from now on.
/// </summary>
/// <remarks>
/// <para>
/// <b>When it is published:</b> only on the transition from <c>Draft</c> to <c>Published</c> (the <c>POST /v1/materials/{id}/publish</c>
/// endpoint). Publishing an already published material succeeds without publishing the event again; an archived material cannot be
/// published. A material is published at most once in its lifetime, so each <see cref="MaterialId"/> appears in this event at most once
/// (apart from redeliveries). Later changes of the title, description or content are <b>not</b> announced by this event.
/// </para>
/// <para>
/// <b>Delivery:</b> the event is written to the transactional outbox of the Knowledge service in the same database transaction as the
/// state change of the material, and <c>Knowledge.Worker</c> delivers it to RabbitMQ afterwards. It is therefore never published for a change
/// that was rolled back, but it is delivered <b>at least once</b> and possibly with a delay: consumers must be idempotent (for example
/// deduplicate by <see cref="MaterialId"/>) and must not assume ordering relative to other Knowledge events.
/// </para>
/// <para>
/// <b>Versioning:</b> only backward-compatible changes (such as adding an optional field) are made to this type. A breaking change
/// is published as a new type (<c>MaterialPublishedV2</c>), with both versions published in parallel during migration.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class MaterialPublishedConsumer : IConsumer&lt;MaterialPublishedV1&gt;
/// {
///     public Task Consume(ConsumeContext&lt;MaterialPublishedV1&gt; context)
///     {
///         // idempotent: upsert by context.Message.MaterialId
///         ...
///     }
/// }
/// </code>
/// </example>
/// <param name="MaterialId">Identifier of the published material (a GUID assigned by the Knowledge service, never <see cref="Guid.Empty"/>).</param>
/// <param name="Type">
/// Material type as its name: <c>Article</c>, <c>Video</c> or <c>Podcast</c> (case as shown). Consumers should tolerate unknown values
/// added in the future.
/// </param>
/// <param name="Title">Title of the material at the time of publishing: non-empty, trimmed plain text without formatting, at most 200 characters.</param>
/// <param name="PublishedAt">
/// Moment of publishing in UTC, exactly as stored on the material: identical to the <c>publishedAt</c> value returned by the API.
/// </param>
public sealed record MaterialPublishedV1(Guid MaterialId, string Type, string Title, DateTimeOffset PublishedAt);
