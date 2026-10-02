namespace Knowledge.Infrastructure.Persistence.Read.Models;

/// <summary>Read model of one row of <c>knowledge.Favorites</c>: a material or collection added to favorites by a user.</summary>
internal sealed class FavoriteRow
{
    /// <summary>Identifier of the favorite (primary key).</summary>
    public Guid Id { get; init; }

    /// <summary>Subject (<c>sub</c> claim) of the user who owns the favorite.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>Kind of the item stored as the enum name: <c>Material</c> or <c>Collection</c>.</summary>
    public string ItemType { get; init; } = string.Empty;

    /// <summary>Identifier of the material or collection; no foreign key, because it may point to either table.</summary>
    public Guid ItemId { get; init; }

    /// <summary>Moment the item was added to favorites.</summary>
    public DateTimeOffset AddedAt { get; init; }
}
