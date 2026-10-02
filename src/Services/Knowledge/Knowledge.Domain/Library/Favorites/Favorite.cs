using SuperApp.Framework.Domain.Aggregates;
using Knowledge.Domain.Collections;
using Knowledge.Domain.Materials;
using Knowledge.Domain.Common;

namespace Knowledge.Domain.Library.Favorites;

/// <summary>
/// Aggregate root of a favorite: one user's bookmark of one published material or collection (part of the user's library, ADR-0028).
/// </summary>
/// <remarks>
/// <para>
/// A deliberately small aggregate: it is created and deleted, never modified, and raises no events. Users manage their own favorites with the
/// <c>knowledge.library.write</c> scope; the owner is always the current user (<see cref="UserId"/>), never a value from the request body.
/// </para>
/// <para><b>Rules enforced outside the aggregate</b> (it cannot see other aggregates):</para>
/// <list type="bullet">
///   <item><description>The item must exist and be published when it is added; the handler checks it with
///   <see cref="Knowledge.Domain.Materials.IMaterialRepository.IsPublishedAsync"/> or
///   <see cref="Knowledge.Domain.Collections.ICollectionRepository.IsPublishedAsync"/> and returns <see cref="LibraryErrors.ItemNotAvailable"/>.</description></item>
///   <item><description>One favorite per user and item: the handler first looks it up with <see cref="IFavoriteRepository.FindAsync"/> and does
///   nothing if it exists (adding is idempotent); a unique index on (user, item type, item ID) backs this up. When two concurrent requests
///   both pass the lookup, the second save hits the index and fails with <see cref="LibraryErrors.FavoriteAddedConcurrently"/>.</description></item>
///   <item><description>When the item is archived, all its favorites are deleted asynchronously by the Worker
///   (<see cref="IFavoriteRepository.RemoveAllForItemAsync"/>).</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // AddFavoriteHandler, after the availability check
/// if (await favorites.FindAsync(userId, command.ItemType, command.ItemId, cancellationToken) is null)
/// {
///     favorites.Add(Favorite.Add(userId, command.ItemType, command.ItemId, clock.UtcNow));
/// }
/// </code>
/// </example>
/// <seealso cref="IFavoriteRepository"/>
public sealed class Favorite : AggregateRoot<FavoriteId>
{
    private Favorite(FavoriteId id)
        : base(id)
    {
    }

    /// <summary>Gets the user who owns this favorite (the <c>sub</c> claim).</summary>
    public UserId UserId { get; private set; }

    /// <summary>Gets the kind of the bookmarked item; tells how to interpret <see cref="ItemId"/>.</summary>
    public FavoriteItemType ItemType { get; private set; }

    /// <summary>
    /// Gets the identifier of the bookmarked material or collection, depending on <see cref="ItemType"/>. A plain <see cref="Guid"/> because
    /// it can hold either a <see cref="MaterialId"/> or a <see cref="CollectionId"/> value.
    /// </summary>
    public Guid ItemId { get; private set; }

    /// <summary>Gets the moment the item was added to favorites.</summary>
    public DateTimeOffset AddedAt { get; private set; }

    /// <summary>
    /// Creates a favorite of an item for a user. Performs no validation and cannot fail: the handler must first check that the item exists
    /// and is published (<see cref="LibraryErrors.ItemNotAvailable"/>) and that the user has not bookmarked it yet.
    /// </summary>
    /// <remarks>The caller registers the result with <see cref="IFavoriteRepository.Add"/>. Raises no events.</remarks>
    /// <param name="userId">The user adding the item (the current user).</param>
    /// <param name="itemType">Kind of the item.</param>
    /// <param name="itemId">Identifier of the material or collection, matching <paramref name="itemType"/>; not checked here.</param>
    /// <param name="now">Current time, stored as <see cref="AddedAt"/>.</param>
    /// <returns>The new favorite.</returns>
    public static Favorite Add(UserId userId, FavoriteItemType itemType, Guid itemId, DateTimeOffset now) =>
        new(FavoriteId.New()) { UserId = userId, ItemType = itemType, ItemId = itemId, AddedAt = now };
}
