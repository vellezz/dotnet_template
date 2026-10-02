namespace Knowledge.Domain.Library.Favorites;

/// <summary>
/// Kind of item a <see cref="Favorite"/> points at; decides whether <see cref="Favorite.ItemId"/> is a material or a collection ID.
/// </summary>
/// <remarks>Stored in the database and exposed in the API by member name; renaming a member is a breaking change.</remarks>
public enum FavoriteItemType
{
    /// <summary>A material (<see cref="Materials.Material"/>).</summary>
    Material,

    /// <summary>A collection (<see cref="Collections.Collection"/>).</summary>
    Collection,
}
