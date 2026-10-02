using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;

namespace Knowledge.Application.Features.Categories.ListCategories;

/// <summary>
/// Returns all catalog categories, sorted by name. Requires scope <see cref="KnowledgeScopes.CatalogRead"/>.
/// </summary>
/// <remarks>
/// <para>
/// The list is not paged (categories are few). The handler lives in Infrastructure (<c>ListCategoriesHandler</c>, ADR-0026); it reads the
/// read model and caches the whole list, which is invalidated whenever a category is created or renamed.
/// </para>
/// <para>
/// Result: the list of <see cref="CategoryDto"/>, possibly empty. Possible errors: <c>auth.unauthenticated</c> (403) and
/// <c>auth.missing_scope</c> (403).
/// </para>
/// </remarks>
[RequiresScope(KnowledgeScopes.CatalogRead)]
public sealed record ListCategories : IQuery<Result<IReadOnlyList<CategoryDto>>>;
