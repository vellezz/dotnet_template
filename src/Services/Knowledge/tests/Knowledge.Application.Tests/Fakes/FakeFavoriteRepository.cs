using Knowledge.Domain.Library.Favorites;
using Knowledge.Domain.Common;

namespace Knowledge.Application.Tests.Fakes;

internal sealed class FakeFavoriteRepository : IFavoriteRepository
{
    public List<Favorite> Items { get; } = [];

    public Task<Favorite?> FindAsync(UserId userId, FavoriteItemType itemType, Guid itemId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(favorite => favorite.UserId == userId && favorite.ItemType == itemType && favorite.ItemId == itemId));

    public void Add(Favorite favorite) => Items.Add(favorite);

    public void Remove(Favorite favorite) => Items.Remove(favorite);

    public Task<int> RemoveAllForItemAsync(FavoriteItemType itemType, Guid itemId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.RemoveAll(favorite => favorite.ItemType == itemType && favorite.ItemId == itemId));
}
