using SuperApp.Framework.Domain.Events;

namespace Knowledge.Domain.Categories.Events;

/// <summary>
/// Domain event: a category was created or renamed. Raised by <see cref="Category.Create"/> and by <see cref="Category.Rename"/> when the
/// name actually changes (renaming to the current name raises nothing).
/// </summary>
/// <remarks>
/// Used only inside the Knowledge context for cache invalidation (ADR-0020): <c>CategoryCacheInvalidation</c> registers the removal of the cached
/// category list with <c>IUnitOfWork.OnCommitted</c>, so the list is removed after the command's transaction commits (and not at all when it
/// rolls back). It is not published as an integration event.
/// </remarks>
/// <param name="CategoryId">Identifier of the created or renamed category.</param>
public sealed record CategoryChanged(CategoryId CategoryId) : IDomainEvent;
