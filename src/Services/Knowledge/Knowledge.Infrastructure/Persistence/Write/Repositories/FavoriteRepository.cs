using Knowledge.Domain.Library.Favorites;
using Knowledge.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Persistence.Write.Repositories;

/// <summary>EF Core implementation of <see cref="IFavoriteRepository"/> over <see cref="KnowledgeWriteDbContext"/>.</summary>
/// <remarks>
/// <c>Add</c> and <c>Remove</c> only change the tracked state; the transaction pipeline behavior saves. <c>RemoveAllForItemAsync</c> is
/// different: it runs a set-based <c>DELETE</c> (<c>ExecuteDeleteAsync</c>) immediately, inside the transaction opened by the pipeline,
/// bypassing change tracking and domain events.
/// </remarks>
/// <param name="context">Write context of the service (scoped, shared with the unit of work).</param>
internal sealed class FavoriteRepository(KnowledgeWriteDbContext context) : IFavoriteRepository
{
    /// <inheritdoc />
    public Task<Favorite?> FindAsync(UserId userId, FavoriteItemType itemType, Guid itemId, CancellationToken cancellationToken) =>
        context.Set<Favorite>().FirstOrDefaultAsync(
            favorite => favorite.UserId == userId && favorite.ItemType == itemType && favorite.ItemId == itemId,
            cancellationToken);

    /// <inheritdoc />
    public void Add(Favorite favorite) => context.Add(favorite);

    /// <inheritdoc />
    public void Remove(Favorite favorite) => context.Remove(favorite);

    /// <inheritdoc />
    public Task<int> RemoveAllForItemAsync(FavoriteItemType itemType, Guid itemId, CancellationToken cancellationToken) =>
        context.Set<Favorite>()
            .Where(favorite => favorite.ItemType == itemType && favorite.ItemId == itemId)
            .ExecuteDeleteAsync(cancellationToken);
}
