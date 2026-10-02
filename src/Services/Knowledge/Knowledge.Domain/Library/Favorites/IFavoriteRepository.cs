using Knowledge.Domain.Common;

namespace Knowledge.Domain.Library.Favorites;

/// <summary>
/// Write-side repository of the <see cref="Favorite"/> aggregate: finds, adds and removes one user's favorites, and removes all favorites
/// of an archived item.
/// </summary>
/// <remarks>
/// Implemented in <c>Knowledge.Infrastructure</c>. <see cref="Add"/> and <see cref="Remove"/> take effect when the unit of work commits;
/// <see cref="RemoveAllForItemAsync"/> executes immediately, see its remarks. Listing a user's favorites for display goes through the read side.
/// </remarks>
public interface IFavoriteRepository
{
    /// <summary>Finds the favorite of a given item owned by a given user.</summary>
    /// <param name="userId">Owner of the favorite (the current user).</param>
    /// <param name="itemType">Kind of the item.</param>
    /// <param name="itemId">Identifier of the material or collection.</param>
    /// <param name="cancellationToken">Token to cancel the database call.</param>
    /// <returns>The tracked favorite, or <see langword="null"/> if the user has not bookmarked the item.</returns>
    Task<Favorite?> FindAsync(UserId userId, FavoriteItemType itemType, Guid itemId, CancellationToken cancellationToken);

    /// <summary>Registers a new favorite; it is inserted when the unit of work commits.</summary>
    /// <param name="favorite">The new favorite, as returned by <see cref="Favorite.Add"/>.</param>
    void Add(Favorite favorite);

    /// <summary>Marks a favorite for deletion; it is deleted when the unit of work commits.</summary>
    /// <param name="favorite">The favorite to delete, previously loaded with <see cref="FindAsync"/>.</param>
    void Remove(Favorite favorite);

    /// <summary>
    /// Deletes the favorites of all users that point at the given item; used after the item has been archived.
    /// </summary>
    /// <remarks>
    /// A deliberate exception to "one transaction modifies one aggregate" (ADR-0028): deleting many <see cref="Favorite"/>s in one operation
    /// breaks no invariant of any of them. It is triggered by the Worker when it consumes <c>MaterialArchivedV1</c> or <c>CollectionArchivedV1</c>.
    /// Unlike <see cref="Remove"/>, the delete is executed immediately as one set-based statement (inside the command's transaction),
    /// without loading the aggregates and without waiting for the unit of work to commit.
    /// </remarks>
    /// <param name="itemType">Kind of the archived item.</param>
    /// <param name="itemId">Identifier of the archived material or collection.</param>
    /// <param name="cancellationToken">Token to cancel the database call.</param>
    /// <returns>Number of deleted favorites (0 when nobody had bookmarked the item, e.g. on redelivery).</returns>
    Task<int> RemoveAllForItemAsync(FavoriteItemType itemType, Guid itemId, CancellationToken cancellationToken);
}
