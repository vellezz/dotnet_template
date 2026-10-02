using Knowledge.Domain.Categories;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Persistence.Write.Repositories;

// One repository per aggregate (write side, no IQueryable exposed). Changes are saved only by the unit of work.

/// <summary>EF Core implementation of <see cref="ICategoryRepository"/> over <see cref="KnowledgeWriteDbContext"/>.</summary>
/// <remarks>
/// Loaded aggregates are tracked by the write context; the command handler changes them and the transaction pipeline behavior saves
/// them with <c>SaveChangesAsync</c>. The repository itself never saves.
/// </remarks>
/// <param name="context">Write context of the service (scoped, shared with the unit of work).</param>
internal sealed class CategoryRepository(KnowledgeWriteDbContext context) : ICategoryRepository
{
    /// <inheritdoc />
    public Task<Category?> GetAsync(CategoryId id, CancellationToken cancellationToken) =>
        context.Set<Category>().FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        context.Set<Category>().AnyAsync(category => category.Slug == slug, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Removes duplicates first and compares the number of matching rows with the number of distinct identifiers, so a repeated
    /// identifier of an existing category does not make the check fail.
    /// </remarks>
    public async Task<bool> AllExistAsync(IReadOnlyCollection<CategoryId> ids, CancellationToken cancellationToken)
    {
        var distinctIds = ids.Distinct().ToList();
        return distinctIds.Count == 0
            || await context.Set<Category>().CountAsync(category => distinctIds.Contains(category.Id), cancellationToken) == distinctIds.Count;
    }

    /// <inheritdoc />
    public void Add(Category category) => context.Add(category);
}
