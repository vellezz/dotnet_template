using Knowledge.Domain.Categories;

namespace Knowledge.Domain.Materials;

/// <summary>
/// Assignment of a <see cref="Material"/> to a category: a value object owned by the material and stored in the
/// <c>MaterialCategories</c> table.
/// </summary>
/// <remarks>
/// Created only by <see cref="Material.SetCategories"/>, which replaces the whole set. It holds just the ID of the category aggregate;
/// the category's name and slug are read on the query side.
/// </remarks>
/// <param name="CategoryId">Identifier of the assigned category.</param>
public sealed record MaterialCategory(CategoryId CategoryId);
