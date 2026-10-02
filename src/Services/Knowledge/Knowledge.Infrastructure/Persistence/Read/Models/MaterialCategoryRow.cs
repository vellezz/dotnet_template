namespace Knowledge.Infrastructure.Persistence.Read.Models;

/// <summary>Read model of one row of <c>knowledge.MaterialCategories</c>: the assignment of a material to a category.</summary>
internal sealed class MaterialCategoryRow
{
    /// <summary>Identifier of the material (first part of the key).</summary>
    public Guid MaterialId { get; init; }

    /// <summary>Identifier of the assigned category (second part of the key); no foreign key to <c>Categories</c>.</summary>
    public Guid CategoryId { get; init; }
}
