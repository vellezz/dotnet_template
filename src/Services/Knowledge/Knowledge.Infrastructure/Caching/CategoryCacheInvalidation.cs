using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Application.Persistence;
using Knowledge.Domain.Categories.Events;
using SuperApp.Framework.Infrastructure.Caching;

namespace Knowledge.Infrastructure.Caching;

/// <summary>
/// Domain event handler that removes the cached category list (<see cref="KnowledgeCache.CategoriesTag"/>) after a category is
/// created or renamed (<see cref="CategoryChanged"/>) and the change is committed.
/// </summary>
/// <remarks>
/// <para>
/// The handler runs inside <c>SaveChangesAsync</c> of the write context, before the transaction commits (ADR-0027), but it does not touch
/// the cache there: it only registers the removal with <see cref="IUnitOfWork.OnCommitted"/>. Removing the entry before the commit would
/// let a concurrent read load the old, still committed list and cache it again, where it would stay until it became stale. Removing it
/// after the commit guarantees that the next read loads the new data (ADR-0020).
/// </para>
/// <para>
/// When the transaction rolls back (a failed save, a failed command), the registered removal is discarded and the cached list, which is
/// still correct, stays. A failing removal after the commit is logged by the unit of work and does not fail the committed command; the
/// entry then expires by itself (see <see cref="KnowledgeCache.Categories"/>). Handlers like this one must not modify aggregates.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Unit of work of the current command (the write context of the same scope) that runs after-commit actions.</param>
/// <param name="cache">Fail-safe cache of the service.</param>
internal sealed class CategoryCacheInvalidation(IUnitOfWork unitOfWork, FailSafeCache cache) : IDomainEventHandler<CategoryChanged>
{
    /// <inheritdoc />
    public Task HandleAsync(CategoryChanged domainEvent, CancellationToken cancellationToken)
    {
        unitOfWork.OnCommitted(token => cache.RemoveByTagAsync(KnowledgeCache.CategoriesTag, token).AsTask());
        return Task.CompletedTask;
    }
}
