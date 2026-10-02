using Knowledge.Domain.Library.Favorites;

namespace Knowledge.Application.Features.Library.ListMyFavorites;

/// <summary>
/// One favorite of the calling user; one item of the <see cref="ListMyFavorites"/> result.
/// </summary>
/// <param name="ItemType">Kind of item: <c>Material</c> or <c>Collection</c> (<see cref="FavoriteItemType"/>, serialized as a string).</param>
/// <param name="ItemId">
/// Identifier of the material or collection, depending on <see cref="ItemType"/>; pass it to <c>GetMaterial</c> or <c>GetCollection</c>.
/// </param>
/// <param name="Title">Current title of the material or collection.</param>
/// <param name="AddedAt">Time when the item was added to favorites.</param>
public sealed record FavoriteDto(FavoriteItemType ItemType, Guid ItemId, string Title, DateTimeOffset AddedAt);
