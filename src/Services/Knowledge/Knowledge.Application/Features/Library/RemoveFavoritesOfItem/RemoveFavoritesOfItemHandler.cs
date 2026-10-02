using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Library.Favorites;

namespace Knowledge.Application.Features.Library.RemoveFavoritesOfItem;

/// <summary>
/// Handles <see cref="RemoveFavoritesOfItem"/>: deletes all favorites of the item with one set-based operation of the repository.
/// </summary>
/// <remarks>
/// <see cref="IFavoriteRepository.RemoveAllForItemAsync"/> executes the delete immediately (it does not wait for <c>SaveChanges</c>),
/// inside the transaction opened by the transaction behavior, so it is still rolled back if the command fails later.
/// The number of deleted rows is ignored.
/// </remarks>
internal sealed class RemoveFavoritesOfItemHandler(IFavoriteRepository favorites) : ICommandHandler<RemoveFavoritesOfItem>
{
    /// <inheritdoc />
    public async Task<Result> Handle(RemoveFavoritesOfItem command, CancellationToken cancellationToken)
    {
        await favorites.RemoveAllForItemAsync(command.ItemType, command.ItemId, cancellationToken);
        return Result.Success();
    }
}
