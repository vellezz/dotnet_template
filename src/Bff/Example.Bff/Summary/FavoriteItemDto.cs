namespace Example.Bff.Summary;

/// <summary>A favorite on the summary screen: what it is and how to open it.</summary>
/// <param name="ItemType">Kind of the item, <c>Material</c> or <c>Collection</c> (as in the Knowledge contract).</param>
/// <param name="ItemId">Identifier of the material or collection, used to open it through <c>/v1/knowledge/...</c>.</param>
/// <param name="Title">Title of the item.</param>
public sealed record FavoriteItemDto(string ItemType, Guid ItemId, string Title);
