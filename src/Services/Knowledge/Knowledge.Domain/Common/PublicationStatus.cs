namespace Knowledge.Domain.Common;

/// <summary>
/// Lifecycle stage of a <see cref="Knowledge.Domain.Materials.Material"/> or a <see cref="Knowledge.Domain.Collections.Collection"/>.
/// </summary>
/// <remarks>
/// <para>
/// The lifecycle is one-way and follows the order of the values: <see cref="Draft"/> → <see cref="Published"/> → <see cref="Archived"/>.
/// A draft may also be archived directly. There is no way back: a published item cannot return to draft (no "unpublish"),
/// and an archived item stays archived forever.
/// </para>
/// <list type="bullet">
///   <item><description><c>Publish</c> moves <see cref="Draft"/> to <see cref="Published"/>; calling it again on a published item is a no-op,
///   calling it on an archived item fails with the aggregate's <c>Archived</c> error.</description></item>
///   <item><description><c>Archive</c> moves any other status to <see cref="Archived"/>; calling it again is a no-op.</description></item>
/// </list>
/// <para>
/// Readers (scope <c>knowledge.catalog.read</c> only) see published items; editors (<c>knowledge.catalog.write</c>) also see drafts and
/// archived items. Only published items can be added to favorites or marked as completed.
/// </para>
/// </remarks>
public enum PublicationStatus
{
    /// <summary>
    /// Initial status of every new material and collection: visible to editors only and freely editable. The publication
    /// requirements (content and main media for a material, at least one item for a collection) are not enforced yet.
    /// </summary>
    Draft,

    /// <summary>
    /// Available to all readers. Still editable, but no change may break the publication requirements
    /// (e.g. removing all content or all collection items is rejected).
    /// </summary>
    Published,

    /// <summary>
    /// Final status: every change and a new publication are rejected with the aggregate's <c>Archived</c> error.
    /// Archiving raises an event that removes the item from all users' favorites.
    /// </summary>
    Archived,
}
