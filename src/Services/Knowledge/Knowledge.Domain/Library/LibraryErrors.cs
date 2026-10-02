using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Library.Completions;
using Knowledge.Domain.Library.Favorites;

namespace Knowledge.Domain.Library;

/// <summary>
/// Catalog of reusable errors of the user's library: favorites (<see cref="Favorite"/>) and completed materials
/// (<see cref="MaterialCompletion"/>) (ADR-0015).
/// </summary>
/// <remarks>
/// Related errors created elsewhere: <c>knowledge.user.invalid_id</c> (<see cref="Knowledge.Domain.Common.UserId.Create"/>) when the
/// <c>sub</c> claim is unusable, and <c>knowledge.favorite.invalid_id</c> / <c>knowledge.completion.invalid_id</c> for empty IDs.
/// </remarks>
public static class LibraryErrors
{
    /// <summary>
    /// <c>knowledge.library.item_not_available</c> (NotFound, HTTP 404): the material or collection to add to favorites, or the material to
    /// mark as completed, does not exist, has an empty ID, or is not published (draft or archived).
    /// </summary>
    /// <remarks>
    /// Returned by the add-favorite and mark-material-completed handlers after <c>IsPublishedAsync</c>. Missing and unpublished items share
    /// one error on purpose, so readers cannot discover drafts.
    /// </remarks>
    public static readonly Error ItemNotAvailable =
        Error.NotFound("knowledge.library.item_not_available", "Element nie istnieje lub nie jest opublikowany.");

    /// <summary>
    /// <c>knowledge.library.favorite_added_concurrently</c> (Conflict, HTTP 409): a concurrent request of the same user added the same item
    /// to favorites between this request's existence check and its save, so this request's insert hit the unique index on
    /// (user, item type, item ID).
    /// </summary>
    /// <remarks>
    /// Only a race outcome: adding an existing favorite sequentially is an idempotent success, never this error. The unit of work returns it
    /// in place of the database exception (the write context maps the index <c>IX_Favorites_UserId_ItemType_ItemId</c> to it). The favorite
    /// exists afterwards, so clients may treat it as success or simply retry, which then succeeds.
    /// </remarks>
    public static readonly Error FavoriteAddedConcurrently =
        Error.Conflict("knowledge.library.favorite_added_concurrently", "Element został równolegle dodany do ulubionych.");

    /// <summary>
    /// <c>knowledge.library.completion_recorded_concurrently</c> (Conflict, HTTP 409): a concurrent request of the same user marked the same
    /// material as completed between this request's existence check and its save, so this request's insert hit the unique index on
    /// (user, material).
    /// </summary>
    /// <remarks>
    /// Only a race outcome: marking an already completed material sequentially is an idempotent success, never this error. The unit of work
    /// returns it in place of the database exception (the write context maps the index <c>IX_MaterialCompletions_UserId_MaterialId</c> to
    /// it). The completion exists afterwards, so clients may treat it as success or simply retry, which then succeeds.
    /// </remarks>
    public static readonly Error CompletionRecordedConcurrently =
        Error.Conflict("knowledge.library.completion_recorded_concurrently", "Materiał został równolegle oznaczony jako ukończony.");
}
