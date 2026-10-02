using Knowledge.Domain.Collections;
using Knowledge.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Persistence.Write.Repositories;

/// <summary>EF Core implementation of <see cref="ICollectionRepository"/> over <see cref="KnowledgeWriteDbContext"/>.</summary>
/// <remarks>
/// <c>GetAsync</c> loads the collection with its owned items and categories (owned collections are included automatically).
/// The repository never saves; the transaction pipeline behavior does.
/// </remarks>
/// <param name="context">Write context of the service (scoped, shared with the unit of work).</param>
internal sealed class CollectionRepository(KnowledgeWriteDbContext context) : ICollectionRepository
{
    /// <inheritdoc />
    public Task<Collection?> GetAsync(CollectionId id, CancellationToken cancellationToken) =>
        context.Set<Collection>().FirstOrDefaultAsync(collection => collection.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsPublishedAsync(CollectionId id, CancellationToken cancellationToken) =>
        context.Set<Collection>().AnyAsync(collection => collection.Id == id && collection.Status == PublicationStatus.Published, cancellationToken);

    /// <inheritdoc />
    public void Add(Collection collection) => context.Add(collection);
}
