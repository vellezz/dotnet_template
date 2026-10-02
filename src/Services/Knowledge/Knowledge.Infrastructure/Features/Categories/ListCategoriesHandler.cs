using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Domain.Results;
using Knowledge.Infrastructure.Caching;
using Knowledge.Infrastructure.Persistence.Read.Models;
using SuperApp.Framework.Infrastructure.Caching;
using Knowledge.Application.Features.Categories.ListCategories;
using Knowledge.Infrastructure.Persistence.Read;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Features.Categories;

/// <summary>
/// Handles <see cref="ListCategories"/>: returns all categories ordered by name, served from the fail-safe cache.
/// </summary>
/// <remarks>
/// The whole list is cached under <see cref="KnowledgeCache.CategoriesKey"/> with the settings <see cref="KnowledgeCache.Categories"/> and
/// invalidated by <see cref="CategoryCacheInvalidation"/> after a command that created or renamed a category commits. The query reads the flat
/// <see cref="CategoryRow"/> read model through <see cref="KnowledgeReadDbContext"/>, never the <c>Category</c> aggregate (ADR-0026).
/// Always succeeds; a database error with no cached value to fall back on is thrown as an exception.
/// </remarks>
/// <param name="db">Read context of the service.</param>
/// <param name="cache">Fail-safe cache of the service.</param>
internal sealed class ListCategoriesHandler(KnowledgeReadDbContext db, FailSafeCache cache)
    : IQueryHandler<ListCategories, Result<IReadOnlyList<CategoryDto>>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(ListCategories query, CancellationToken cancellationToken)
    {
        var categories = await cache.GetOrCreateAsync(
            KnowledgeCache.CategoriesKey,
            async token => (IReadOnlyList<CategoryDto>)await db.Categories
                .OrderBy(category => category.Name)
                .Select(category => new CategoryDto(category.Id, category.Name, category.Slug))
                .ToListAsync(token),
            KnowledgeCache.Categories,
            [KnowledgeCache.CategoriesTag],
            cancellationToken);

        return Result<IReadOnlyList<CategoryDto>>.Success(categories);
    }
}
