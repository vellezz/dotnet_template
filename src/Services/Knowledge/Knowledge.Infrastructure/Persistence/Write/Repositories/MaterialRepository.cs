using Knowledge.Domain.Common;
using Knowledge.Domain.Materials;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Persistence.Write.Repositories;

/// <summary>EF Core implementation of <see cref="IMaterialRepository"/> over <see cref="KnowledgeWriteDbContext"/>.</summary>
/// <remarks>
/// <c>GetAsync</c> loads the whole aggregate, including all content blocks and their spans, using a split query to avoid a cartesian
/// explosion of rows; this is needed because content is always replaced as a whole. The existence checks do not load aggregates.
/// The repository never saves; the transaction pipeline behavior does.
/// </remarks>
/// <param name="context">Write context of the service (scoped, shared with the unit of work).</param>
internal sealed class MaterialRepository(KnowledgeWriteDbContext context) : IMaterialRepository
{
    /// <inheritdoc />
    public Task<Material?> GetAsync(MaterialId id, CancellationToken cancellationToken) =>
        context.Set<Material>()
            .Include(material => material.Blocks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(material => material.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Removes duplicates first and compares the number of matching rows with the number of distinct identifiers, so a repeated
    /// identifier of an existing material does not make the check fail.
    /// </remarks>
    public async Task<bool> AllExistAsync(IReadOnlyCollection<MaterialId> ids, CancellationToken cancellationToken)
    {
        var distinctIds = ids.Distinct().ToList();
        return distinctIds.Count == 0
            || await context.Set<Material>().CountAsync(material => distinctIds.Contains(material.Id), cancellationToken) == distinctIds.Count;
    }

    /// <inheritdoc />
    public Task<bool> IsPublishedAsync(MaterialId id, CancellationToken cancellationToken) =>
        context.Set<Material>().AnyAsync(material => material.Id == id && material.Status == PublicationStatus.Published, cancellationToken);

    /// <inheritdoc />
    public void Add(Material material) => context.Add(material);
}
