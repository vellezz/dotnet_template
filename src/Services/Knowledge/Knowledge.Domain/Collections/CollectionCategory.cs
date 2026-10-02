using Knowledge.Domain.Categories;

namespace Knowledge.Domain.Collections;

/// <summary>
/// Assignment of a <see cref="Collection"/> to a category: a value object owned by the collection and stored in the
/// <c>CollectionCategories</c> table.
/// </summary>
/// <remarks>Created only by <see cref="Collection.SetCategories"/>, which replaces the whole set; holds just the category ID.</remarks>
/// <param name="CategoryId">Identifier of the assigned category.</param>
public sealed record CollectionCategory(CategoryId CategoryId);
