using SuperApp.Framework.Domain.ValueObjects;

namespace SuperApp.Framework.Domain.Aggregates;

/// <summary>
/// Base class for domain entities: objects defined by their identity rather than by their attribute values.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="Entity{TId}"/> directly only for child entities that live inside an aggregate and are changed exclusively
/// through the aggregate root (for example a content block of a material). Anything that is loaded, saved and referenced
/// on its own is an aggregate and derives from <see cref="AggregateRoot{TId}"/> instead.
/// </para>
/// <para>
/// The class deliberately does not override <see cref="object.Equals(object)"/>: EF Core tracks entities by reference, and
/// comparing entities is rarely needed in the domain. Compare identifiers (<see cref="Id"/>) explicitly when you need to.
/// </para>
/// </remarks>
/// <typeparam name="TId">
/// Strongly typed identifier of the entity (<see cref="IStronglyTypedId{TSelf, TValue}"/>, ADR-0023). Using a dedicated ID type
/// per entity prevents passing, for example, a <c>CollectionId</c> where a <c>MaterialId</c> is expected.
/// </typeparam>
/// <seealso cref="AggregateRoot{TId}"/>
public abstract class Entity<TId>
    where TId : struct, IEquatable<TId>
{
    /// <summary>Initializes the entity with its identity.</summary>
    /// <param name="id">Identity of the entity; it never changes afterwards.</param>
    protected Entity(TId id) => Id = id;

    /// <summary>Identity of the entity.</summary>
    /// <remarks>The private setter exists only so that EF Core can materialize the entity; domain code never reassigns it.</remarks>
    public TId Id { get; private set; }
}
