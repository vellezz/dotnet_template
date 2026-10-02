using SuperApp.Framework.Application.Persistence;
using SuperApp.Framework.Domain.Events;

namespace SuperApp.Framework.Application.Events;

/// <summary>
/// Delivers a domain event to every <see cref="IDomainEventHandler{TEvent}"/> registered for its runtime type.
/// </summary>
/// <remarks>
/// <para>
/// This is an infrastructure port: application code never calls it. The write database context (<c>WriteDbContextBase</c>) calls it
/// for each event collected from tracked aggregates during <c>SaveChangesAsync</c>, before the changes are written (ADR-0027).
/// The implementation resolves handlers from the current DI scope, so they share the scoped database context and transaction.
/// </para>
/// <para>
/// The framework uses its own dispatcher instead of MediatR notifications on purpose: MediatR is reserved for commands and queries,
/// and domain events must be handled inside the unit of work rather than whenever someone publishes them.
/// </para>
/// </remarks>
/// <seealso cref="IDomainEventHandler{TEvent}"/>
/// <seealso cref="IUnitOfWork"/>
public interface IDomainEventDispatcher
{
    /// <summary>Invokes all handlers registered for the runtime type of <paramref name="domainEvent"/>, one after another.</summary>
    /// <param name="domainEvent">The event to deliver; handlers are selected by its runtime type, not by the static type of the variable.</param>
    /// <param name="cancellationToken">Cancellation of the surrounding request or message.</param>
    /// <returns>
    /// A task that completes when all handlers have finished. If any handler throws, the exception propagates and the unit of work
    /// does not save anything.
    /// </returns>
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);
}
