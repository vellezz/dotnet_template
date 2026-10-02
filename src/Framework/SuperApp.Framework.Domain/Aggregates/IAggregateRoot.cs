using SuperApp.Framework.Domain.Events;

namespace SuperApp.Framework.Domain.Aggregates;

/// <summary>
/// Non-generic view of an aggregate root, used by infrastructure to read and clear domain events without knowing the ID type.
/// </summary>
/// <remarks>
/// Application and domain code do not implement or call this interface directly; derive aggregates from
/// <see cref="AggregateRoot{TId}"/>, which implements it. The write database context (<c>WriteDbContextBase</c>) finds all tracked
/// entities implementing <see cref="IAggregateRoot"/>, takes their <see cref="DomainEvents"/>, calls <see cref="ClearDomainEvents"/>
/// and dispatches the events before saving (ADR-0027).
/// </remarks>
/// <seealso cref="AggregateRoot{TId}"/>
/// <seealso cref="IDomainEvent"/>
public interface IAggregateRoot
{
    /// <summary>Domain events raised since the events were last cleared, in the order they were raised.</summary>
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    /// <summary>Removes all collected domain events.</summary>
    /// <remarks>
    /// The unit of work calls this right after taking the events and before dispatching them, so that a later save of the same
    /// aggregate does not handle them again. Dispatch is a single pass: an event raised while handlers are running is not
    /// dispatched in the same save, which is one of the reasons handlers must not modify aggregates (ADR-0027).
    /// </remarks>
    void ClearDomainEvents();
}
