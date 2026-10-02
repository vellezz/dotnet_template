namespace Knowledge.Infrastructure.Persistence.Read.Models;

/// <summary>Read model of one row of <c>knowledge.CollectionCategories</c>: the assignment of a collection to a category.</summary>
internal sealed class CollectionCategoryRow
{
    /// <summary>Identifier of the collection (first part of the key).</summary>
    public Guid CollectionId { get; init; }

    /// <summary>Identifier of the assigned category (second part of the key); no foreign key to <c>Categories</c>.</summary>
    public Guid CategoryId { get; init; }
}
