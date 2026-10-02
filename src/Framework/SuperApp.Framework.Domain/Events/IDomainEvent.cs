using SuperApp.Framework.Domain.Aggregates;

namespace SuperApp.Framework.Domain.Events;

/// <summary>
/// Marks a type as a domain event: an immutable fact, expressed in the ubiquitous language of a bounded context,
/// stating that something meaningful has just happened inside an aggregate (for example <c>MaterialPublished</c>).
/// </summary>
/// <remarks>
/// <para>
/// Domain events are the mechanism for reacting to changes of an aggregate without coupling the aggregate to the reaction.
/// The aggregate records the event with <see cref="AggregateRoot{TId}.Raise(IDomainEvent)"/>; it never calls handlers itself.
/// </para>
/// <para>Lifecycle of a domain event (ADR-0027):</para>
/// <list type="number">
///   <item><description>An aggregate method changes state and calls <c>Raise(new SomethingHappened(...))</c>.</description></item>
///   <item><description>The command handler finishes; the transaction pipeline behavior calls <c>IUnitOfWork.SaveChangesAsync</c>.</description></item>
///   <item><description>The unit of work collects events from all tracked aggregates, clears them and passes each one to
///   <c>IDomainEventDispatcher</c>, which invokes every <c>IDomainEventHandler&lt;TEvent&gt;</c> registered for the event type.</description></item>
///   <item><description>Only then are the changes written to the database, in the same transaction. A handler that throws rolls back the whole command.</description></item>
/// </list>
/// <para>Rules:</para>
/// <list type="bullet">
///   <item><description>Name events in the past tense, using domain vocabulary (<c>SleepEntryRecorded</c>, not <c>SleepEntryInsert</c>).</description></item>
///   <item><description>Declare them as <c>sealed record</c> types in the <c>Domain</c> project, next to the aggregate that raises them.</description></item>
///   <item><description>Carry the data a handler needs (typically the aggregate ID plus the changed values, and the time of the change taken from the aggregate) so handlers do not have to reload the aggregate or read the clock.</description></item>
///   <item><description>Domain events are internal to one bounded context and may change freely. To inform other services, translate the event
///   in a handler into an integration event from the <c>{Service}.Contracts</c> project, which is a versioned public contract.</description></item>
///   <item><description>Do not use MediatR <c>INotification</c> for domain events; analyzer APP003 reports it as an error.</description></item>
/// </list>
/// </remarks>
/// <example>
/// Declaring and raising an event in an aggregate:
/// <code>
/// public sealed record MaterialPublished(MaterialId MaterialId, MaterialType Type, string Title, DateTimeOffset PublishedAt) : IDomainEvent;
///
/// public Result Publish(DateTimeOffset now)
/// {
///     if (Status == PublicationStatus.Archived)
///     {
///         return MaterialErrors.Archived;
///     }
///
///     Status = PublicationStatus.Published;
///     PublishedAt = now;
///     Raise(new MaterialPublished(Id, Type, Title, now));
///     return Result.Success();
/// }
/// </code>
/// </example>
/// <seealso cref="AggregateRoot{TId}"/>
/// <seealso cref="IAggregateRoot"/>
public interface IDomainEvent;
