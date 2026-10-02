using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Domain.Results;

namespace SuperApp.Framework.Application.Persistence;

/// <summary>
/// The single way changes made by a command reach the database: it dispatches domain events and saves all tracked changes,
/// including outbox messages, in one operation.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by the service's write database context (<c>WriteDbContextBase</c>). Application code does not call it: the transaction
/// pipeline behavior opens a transaction before the command handler, calls <see cref="SaveChangesAsync"/> after a successful result,
/// and commits (ADR-0017, ADR-0027). Repositories only add or modify tracked entities.
/// </para>
/// <para>
/// Keeping saving out of handlers guarantees that every command is atomic, that domain events are always dispatched,
/// and that a failed result never leaves partial changes behind. Analyzer APP003 forbids the synchronous <c>DbContext.SaveChanges</c>.
/// </para>
/// <para>
/// Work that must only happen once the change is durable, such as invalidating cached read models, is registered with
/// <see cref="OnCommitted"/> from a domain event handler instead of being done directly during the save.
/// </para>
/// </remarks>
/// <seealso cref="IUnitOfWorkTransaction"/>
/// <seealso cref="IDomainEventDispatcher"/>
public interface IUnitOfWork
{
    /// <summary>Gets a value indicating whether a database transaction is already open on this unit of work.</summary>
    /// <remarks>
    /// <see langword="true"/> when a command runs inside a MassTransit consumer: the consumer's inbox/outbox has already started a transaction,
    /// so the transaction behavior joins it instead of opening a nested one.
    /// </remarks>
    bool HasActiveTransaction { get; }

    /// <summary>Opens a database transaction.</summary>
    /// <param name="cancellationToken">Cancellation of the surrounding request or message.</param>
    /// <returns>
    /// The open transaction. Call <see cref="IUnitOfWorkTransaction.CommitAsync"/> to make changes durable; disposing it without committing rolls back.
    /// </returns>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Dispatches the domain events collected from tracked aggregates to their handlers, then writes all pending changes
    /// (aggregates and outbox messages) to the database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A unique index or key violation is not an exception here but a failed result: two concurrent requests creating the same thing
    /// (the same slug, the same favorite, the same diary day) is an expected race, and the loser must get the same
    /// <see cref="ErrorType.Conflict"/> error it would get without the race. The service maps index names to its own errors
    /// (for example <c>IX_Categories_Slug</c> to <c>knowledge.category.slug_taken</c>); unmapped indexes produce <c>persistence.duplicate</c>.
    /// After a failed save the pending changes are discarded, so the transaction can still be committed by a surrounding consumer.
    /// </para>
    /// <para>
    /// An optimistic concurrency conflict (another command changed the same aggregate after it was loaded, detected by its <c>rowversion</c>)
    /// is returned as the <see cref="ErrorType.Conflict"/> error <c>persistence.concurrency_conflict</c> when this unit of work opened the
    /// transaction (an HTTP request: the client reloads and retries). Inside a transaction opened by someone else (a MassTransit consumer)
    /// the <c>DbUpdateConcurrencyException</c> is rethrown, so the message is retried with freshly loaded data instead of being
    /// acknowledged as a business failure.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the surrounding request or message.</param>
    /// <returns>
    /// A successful result when the changes are written (they become durable only when the surrounding transaction commits), or a
    /// <see cref="ErrorType.Conflict"/> error when a unique index or key was violated (the service's mapped error or <c>persistence.duplicate</c>)
    /// or, in a transaction this unit of work opened, when a concurrent change was detected (<c>persistence.concurrency_conflict</c>).
    /// </returns>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Forgets every change tracked since the last save (added, modified and removed entities) and the actions registered with
    /// <see cref="OnCommitted"/>, so that a later save in the same scope cannot write them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the transaction behavior when a command fails inside a transaction it does not own. In a MassTransit consumer the
    /// EF inbox/outbox saves and commits its own state after the consumer returns; without discarding, a handler that modified an
    /// aggregate and then returned a failed result would have that partial change (and its domain events) written by that save.
    /// When the behavior owns the transaction it does not need this: the transaction rolls back and the scope ends.
    /// </para>
    /// <para>
    /// Infrastructure state that must survive the failure, such as MassTransit's inbox row for the message being consumed, is kept.
    /// Entities read by the failed command become detached; do not use them afterwards in the same scope.
    /// </para>
    /// </remarks>
    void DiscardChanges();

    /// <summary>
    /// Registers work to run after the current transaction commits, for example invalidating cached read models affected by the change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Domain event handlers run before the commit. Invalidating a cache there would let a concurrent read put the old data back into
    /// the cache before the new data is committed; registering the invalidation here closes that window (ADR-0020, ADR-0027).
    /// </para>
    /// <para>
    /// The actions run in registration order after the commit of the transaction the unit of work takes part in, whether the transaction
    /// was opened by the transaction behavior or by a MassTransit consumer. They are discarded when the transaction rolls back.
    /// A failing action is logged and does not affect the already committed command; keep actions idempotent and short.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// internal sealed class CategoryCacheInvalidation(IUnitOfWork unitOfWork, FailSafeCache cache) : IDomainEventHandler&lt;CategoryChanged&gt;
    /// {
    ///     public Task HandleAsync(CategoryChanged domainEvent, CancellationToken cancellationToken)
    ///     {
    ///         unitOfWork.OnCommitted(token =&gt; cache.RemoveByTagAsync(KnowledgeCache.CategoriesTag, token).AsTask());
    ///         return Task.CompletedTask;
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <param name="action">The work to run after commit; receives a cancellation token that is not bound to the original request.</param>
    void OnCommitted(Func<CancellationToken, Task> action);
}
