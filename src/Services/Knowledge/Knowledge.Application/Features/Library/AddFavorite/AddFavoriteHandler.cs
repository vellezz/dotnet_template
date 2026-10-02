using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Library.Favorites;
using Knowledge.Domain.Collections;
using Knowledge.Domain.Library;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Library.AddFavorite;

/// <summary>
/// Handles <see cref="AddFavorite"/>: resolves the calling user, checks that the item is published and adds a <see cref="Favorite"/>
/// if the user does not have one for the item yet.
/// </summary>
/// <remarks>
/// Only the publication status of the target is read (<c>IsPublishedAsync</c>); the material or collection aggregate is not loaded or
/// changed. Uniqueness per user and item is checked here and enforced by a unique index; two concurrent requests for the same item may
/// still collide on that index, and the unit of work then returns <see cref="LibraryErrors.FavoriteAddedConcurrently"/> (409) instead of
/// the idempotent success a sequential duplicate gets. A race cannot be answered with success here, because the save fails after the
/// handler has returned; the conflict error tells the client that the favorite exists.
/// </remarks>
internal sealed class AddFavoriteHandler(
    IFavoriteRepository favorites,
    IMaterialRepository materials,
    ICollectionRepository collections,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<AddFavorite>
{
    /// <inheritdoc />
    public async Task<Result> Handle(AddFavorite command, CancellationToken cancellationToken)
    {
        if (!currentUser.RequireUserId().TryGetValue(out var userId, out var userError))
        {
            return userError;
        }

        var available = command.ItemType switch
        {
            FavoriteItemType.Material => MaterialId.Create(command.ItemId).TryGetValue(out var materialId, out _)
                && await materials.IsPublishedAsync(materialId, cancellationToken),
            _ => CollectionId.Create(command.ItemId).TryGetValue(out var collectionId, out _)
                && await collections.IsPublishedAsync(collectionId, cancellationToken),
        };
        if (!available)
        {
            return LibraryErrors.ItemNotAvailable;
        }

        if (await favorites.FindAsync(userId, command.ItemType, command.ItemId, cancellationToken) is null)
        {
            favorites.Add(Favorite.Add(userId, command.ItemType, command.ItemId, clock.UtcNow));
        }

        return Result.Success();
    }
}
