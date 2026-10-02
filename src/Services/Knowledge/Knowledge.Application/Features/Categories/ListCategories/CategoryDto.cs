namespace Knowledge.Application.Features.Categories.ListCategories;

/// <summary>
/// A catalog category as returned by <see cref="ListCategories"/>.
/// </summary>
/// <remarks>Read model built directly from the read database; clients use it to show filters and to resolve category IDs of materials.</remarks>
/// <param name="Id">Identifier of the category; use it as <c>CategoryId</c> filter in list queries and in <c>Set...Categories</c> commands.</param>
/// <param name="Name">Display name of the category (trimmed, at most <see cref="Knowledge.Domain.Categories.Category.MaxNameLength"/> characters).</param>
/// <param name="Slug">Unique, immutable URL identifier: lowercase letters, digits and hyphens (for example <c>healthy-sleep</c>).</param>
public sealed record CategoryDto(Guid Id, string Name, string Slug);
