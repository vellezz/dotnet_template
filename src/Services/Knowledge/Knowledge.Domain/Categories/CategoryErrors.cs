using SuperApp.Framework.Domain.Results;

namespace Knowledge.Domain.Categories;

/// <summary>
/// Catalog of reusable errors for the <see cref="Category"/> aggregate and for use cases that reference categories (ADR-0015).
/// </summary>
/// <remarks>
/// Aggregate methods and handlers return these fields directly (implicit conversion to <see cref="Result"/>); the API maps the
/// <see cref="ErrorType"/> to the HTTP status. Codes are a public contract and must not change. Errors created inline elsewhere:
/// <c>knowledge.category.invalid_name</c> (<see cref="Category.Create"/>, <see cref="Category.Rename"/>) and
/// <c>knowledge.category.invalid_id</c> (<see cref="Knowledge.Domain.Categories.CategoryId.Create"/>).
/// </remarks>
public static class CategoryErrors
{
    /// <summary>
    /// <c>knowledge.category.invalid_slug</c> (Validation, HTTP 400): the slug is empty, longer than <see cref="Category.MaxSlugLength"/>,
    /// or not made of lowercase letters and digits separated by single hyphens. Returned by <see cref="Category.Create"/>.
    /// </summary>
    public static readonly Error InvalidSlug =
        Error.Validation("knowledge.category.invalid_slug", "Slug może zawierać wyłącznie małe litery, cyfry i myślniki.");

    /// <summary>
    /// <c>knowledge.category.slug_taken</c> (Conflict, HTTP 409): another category already uses the slug. Returned by the create-category
    /// handler after <see cref="ICategoryRepository.SlugExistsAsync"/>, and by the unit of work when two concurrent creations with the same
    /// slug collide on the unique index of the slug.
    /// </summary>
    public static readonly Error SlugTaken = Error.Conflict("knowledge.category.slug_taken", "Kategoria o tym slugu już istnieje.");

    /// <summary>
    /// <c>knowledge.category.not_found</c> (NotFound, HTTP 404): no category with the given ID exists (or the ID is empty).
    /// Returned by the rename-category handler.
    /// </summary>
    public static readonly Error NotFound = Error.NotFound("knowledge.category.not_found", "Kategoria nie istnieje.");

    /// <summary>
    /// <c>knowledge.category.unknown</c> (Validation, HTTP 400): when setting the categories of a material or collection, at least one of the
    /// given category IDs is empty or does not exist. Returned by the handlers after <see cref="ICategoryRepository.AllExistAsync"/>.
    /// </summary>
    public static readonly Error UnknownCategories = Error.Validation("knowledge.category.unknown", "Co najmniej jedna kategoria nie istnieje.");
}
