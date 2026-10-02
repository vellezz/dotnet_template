using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Application.Persistence;
using Knowledge.Domain.Materials.Events;
using SuperApp.Framework.Infrastructure.Caching;

namespace Knowledge.Infrastructure.Caching;

/// <summary>
/// Domain event handler that removes the cached reader view of a material (<see cref="KnowledgeCache.MaterialTag"/>) after every committed
/// change of that material (<see cref="MaterialChanged"/>: details, content, categories, publishing, archiving).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MaterialChanged"/> is raised at most once per material per save, so one save registers at most one invalidation. The
/// handler runs inside <c>SaveChangesAsync</c> before the commit (ADR-0027) and only registers the removal with
/// <see cref="IUnitOfWork.OnCommitted"/>; the entry is removed after the transaction commits. Removing it earlier would let a concurrent
/// reader cache the old, still committed view again, where it would stay until it became stale (see <see cref="KnowledgeCache.PublishedMaterial"/>).
/// </para>
/// <para>
/// A rolled-back change discards the registered removal, so the cached view (still correct) is kept. A failing removal after the commit
/// is logged by the unit of work and does not fail the committed command. Collections are not cached, so changes of collections need no
/// invalidation.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Unit of work of the current command (the write context of the same scope) that runs after-commit actions.</param>
/// <param name="cache">Fail-safe cache of the service.</param>
internal sealed class MaterialCacheInvalidation(IUnitOfWork unitOfWork, FailSafeCache cache) : IDomainEventHandler<MaterialChanged>
{
    /// <inheritdoc />
    public Task HandleAsync(MaterialChanged domainEvent, CancellationToken cancellationToken)
    {
        var tag = KnowledgeCache.MaterialTag(domainEvent.MaterialId.Value);
        unitOfWork.OnCommitted(token => cache.RemoveByTagAsync(tag, token).AsTask());
        return Task.CompletedTask;
    }
}
