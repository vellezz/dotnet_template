using Knowledge.Domain.Library.Completions;
using Knowledge.Domain.Materials;
using Knowledge.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Persistence.Write.Repositories;

/// <summary>EF Core implementation of <see cref="IMaterialCompletionRepository"/> over <see cref="KnowledgeWriteDbContext"/>.</summary>
/// <remarks>The repository never saves; the transaction pipeline behavior does.</remarks>
/// <param name="context">Write context of the service (scoped, shared with the unit of work).</param>
internal sealed class MaterialCompletionRepository(KnowledgeWriteDbContext context) : IMaterialCompletionRepository
{
    /// <inheritdoc />
    public Task<MaterialCompletion?> FindAsync(UserId userId, MaterialId materialId, CancellationToken cancellationToken) =>
        context.Set<MaterialCompletion>().FirstOrDefaultAsync(
            completion => completion.UserId == userId && completion.MaterialId == materialId,
            cancellationToken);

    /// <inheritdoc />
    public void Add(MaterialCompletion completion) => context.Add(completion);

    /// <inheritdoc />
    public void Remove(MaterialCompletion completion) => context.Remove(completion);
}
