using SuperApp.Framework.Domain.Events;
using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Domain.ValueObjects;

namespace SuperApp.Framework.Domain.Aggregates;

/// <summary>
/// Base class for aggregate roots: the only entry point for changing a consistency boundary of the domain model.
/// Holds the aggregate identity and collects domain events raised by its methods.
/// </summary>
/// <remarks>
/// <para>
/// An aggregate is a cluster of objects (the root, child entities, value objects) that must always be consistent together.
/// All changes go through methods on the root, named after domain operations (<c>Publish</c>, <c>Rename</c>, <c>Archive</c>),
/// which check invariants and return <see cref="Result"/> or <see cref="Result{T}"/> instead of throwing for business rule violations (ADR-0015).
/// </para>
/// <para>How to write an aggregate in this code base:</para>
/// <list type="bullet">
///   <item><description>Declare it as <c>public sealed class Order : AggregateRoot&lt;OrderId&gt;</c> in the <c>Domain</c> project.</description></item>
///   <item><description>Properties have private setters; collections are private fields exposed as <see cref="IReadOnlyList{T}"/>.</description></item>
///   <item><description>Create new instances only through a static factory (<c>Create(...)</c>) returning <see cref="Result{T}"/>;
///   keep a private constructor taking the ID, which EF Core also uses for materialization.</description></item>
///   <item><description>Every state change that other parts of the system may care about calls <see cref="Raise(IDomainEvent)"/>.</description></item>
///   <item><description>One command modifies exactly one aggregate. Consistency with other aggregates is eventual, through domain or integration events.</description></item>
///   <item><description>No references to EF Core, MediatR, ASP.NET or any infrastructure; the architecture tests fail otherwise.</description></item>
///   <item><description>Persist it through a repository interface declared in <c>Domain</c> (one per aggregate, e.g. <c>ICategoryRepository</c>);
///   the command handler never calls <c>SaveChanges</c>, the transaction pipeline behavior does it.</description></item>
/// </list>
/// </remarks>
/// <example>
/// A minimal aggregate with a factory, a domain operation and an event:
/// <code>
/// public sealed class Category : AggregateRoot&lt;CategoryId&gt;
/// {
///     public const int MaxNameLength = 100;
///
///     private Category(CategoryId id)
///         : base(id)
///     {
///     }
///
///     public string Name { get; private set; } = string.Empty;
///
///     public static Result&lt;Category&gt; Create(string name)
///     {
///         if (string.IsNullOrWhiteSpace(name) || name.Length &gt; MaxNameLength)
///         {
///             return Error.Validation("knowledge.category.invalid_name", "Name is required.");
///         }
///
///         var category = new Category(CategoryId.New()) { Name = name };
///         category.Raise(new CategoryChanged(category.Id));
///         return category;
///     }
/// }
/// </code>
/// </example>
/// <typeparam name="TId">
/// Strongly typed identifier of the aggregate, a <c>readonly record struct</c> implementing
/// <see cref="IStronglyTypedId{TSelf, TValue}"/> (ADR-0023).
/// </typeparam>
/// <param name="id">
/// Identity of the aggregate, fixed for its whole lifetime. New aggregates get it from <c>TId.New()</c>;
/// EF Core supplies the stored value when loading.
/// </param>
/// <seealso cref="Entity{TId}"/>
/// <seealso cref="IDomainEvent"/>
public abstract class AggregateRoot<TId>(TId id) : Entity<TId>(id), IAggregateRoot
    where TId : struct, IEquatable<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <inheritdoc />
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    /// <inheritdoc />
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Records a domain event describing a state change that has just happened in this aggregate.
    /// </summary>
    /// <remarks>
    /// The event is not handled immediately. It is kept in <see cref="DomainEvents"/> until the unit of work saves the command's
    /// changes; then every registered <c>IDomainEventHandler&lt;TEvent&gt;</c> is invoked inside the same database transaction (ADR-0027).
    /// Call it after the state has been changed and all invariants have passed, so that a failed operation never leaves a stray event behind.
    /// </remarks>
    /// <param name="domainEvent">The event to record; usually a new <c>sealed record</c> instance carrying the aggregate ID and the changed data.</param>
    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
