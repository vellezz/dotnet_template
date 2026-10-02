using SuperApp.Framework.Domain.Aggregates;

namespace SuperApp.Framework.Domain.ValueObjects;

/// <summary>
/// Contract for a strongly typed identifier of an aggregate or entity, such as <c>MaterialId</c> or <c>SleepEntryId</c>.
/// It is a single-value object that can also generate new identifiers.
/// </summary>
/// <remarks>
/// <para>
/// Strongly typed IDs make the compiler reject mix-ups that plain <see cref="Guid"/> values allow, e.g. passing a collection ID to a method
/// expecting a material ID (ADR-0023). Every aggregate and child entity has its own ID type, written by hand (no source generator).
/// </para>
/// <para>How to write one:</para>
/// <list type="bullet">
///   <item><description>Declare a <c>readonly record struct</c> in the <c>Domain</c> project, next to the aggregate it identifies.</description></item>
///   <item><description>Implement <see cref="New"/> with <see cref="Guid.CreateVersion7()"/>: version 7 GUIDs are time-ordered, which keeps
///   clustered indexes in SQL Server from fragmenting.</description></item>
///   <item><description>Implement <c>Create</c> rejecting <see cref="Guid.Empty"/> with a context-specific validation error, e.g. <c>knowledge.category.invalid_id</c>.</description></item>
///   <item><description>Commands and API use the raw <see cref="Guid"/>; the handler converts it with <c>Create</c> (untrusted input) and the repository
///   receives the typed ID.</description></item>
/// </list>
/// <para>Persistence, JSON and OpenAPI mapping is automatic, as for any <see cref="ISingleValueObject{TSelf, TValue}"/>.</para>
/// </remarks>
/// <example>
/// <code>
/// public readonly record struct CategoryId : IStronglyTypedId&lt;CategoryId, Guid&gt;
/// {
///     private CategoryId(Guid value) =&gt; Value = value;
///
///     public Guid Value { get; }
///
///     public static CategoryId New() =&gt; new(Guid.CreateVersion7());
///
///     public static Result&lt;CategoryId&gt; Create(Guid value) =&gt;
///         value == Guid.Empty
///             ? Error.Validation("knowledge.category.invalid_id", "Invalid category identifier.")
///             : new CategoryId(value);
///
///     public static CategoryId FromTrusted(Guid value) =&gt; new(value);
///
///     public override string ToString() =&gt; Value.ToString();
/// }
/// </code>
/// </example>
/// <typeparam name="TSelf">The implementing identifier type itself (a <c>readonly record struct</c>).</typeparam>
/// <typeparam name="TValue">The primitive the identifier is stored and serialized as; in this code base always <see cref="Guid"/>.</typeparam>
/// <seealso cref="AggregateRoot{TId}"/>
public interface IStronglyTypedId<TSelf, TValue> : ISingleValueObject<TSelf, TValue>
    where TSelf : struct, IStronglyTypedId<TSelf, TValue>
    where TValue : notnull
{
    /// <summary>Generates a new, unique identifier for an aggregate or entity that is being created.</summary>
    /// <returns>A new identifier; implementations use time-ordered GUID version 7.</returns>
    static abstract TSelf New();
}
