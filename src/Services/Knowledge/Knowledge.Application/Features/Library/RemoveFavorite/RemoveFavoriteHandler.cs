using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Library.Favorites;

namespace Knowledge.Application.Features.Library.RemoveFavorite;

/// <summary>
/// Handles <see cref="RemoveFavorite"/>: resolves the calling user and removes that user's <see cref="Favorite"/> for the item, if any.
/// </summary>
internal sealed class RemoveFavoriteHandler(IFavoriteRepository favorites, ICurrentUser currentUser) : ICommandHandler<RemoveFavorite>
{
    /// <inheritdoc />
    public async Task<Result> Handle(RemoveFavorite command, CancellationToken cancellationToken)
    {
        if (!currentUser.RequireUserId().TryGetValue(out var userId, out var userError))
        {
            return userError;
        }

        if (await favorites.FindAsync(userId, command.ItemType, command.ItemId, cancellationToken) is { } favorite)
        {
            favorites.Remove(favorite);
        }

        return Result.Success();
    }
}
