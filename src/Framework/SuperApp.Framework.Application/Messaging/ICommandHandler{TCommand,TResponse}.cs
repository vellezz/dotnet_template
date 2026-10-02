using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Domain.Results;
using MediatR;

namespace SuperApp.Framework.Application.Messaging;

/// <summary>
/// Handles one command type that returns a value: loads the aggregate, asks it to perform the domain operation and returns the outcome.
/// </summary>
/// <remarks>
/// <para>A command handler is an orchestrator, not the place for business logic. A typical handler:</para>
/// <list type="number">
///   <item><description>Converts primitive inputs to strongly typed IDs and value objects with their <c>Create</c> factories and
///   <c>Result&lt;T&gt;.TryGetValue</c>, which names the created value (not the result wrapping it).</description></item>
///   <item><description>Loads the aggregate through its repository (or checks preconditions such as uniqueness through the repository).</description></item>
///   <item><description>Calls one method of the aggregate (or its static factory) and returns the resulting <see cref="Result"/>.</description></item>
///   <item><description>Adds a new aggregate to the repository when it was created.</description></item>
/// </list>
/// <para>What a handler must not do:</para>
/// <list type="bullet">
///   <item><description>Implement rules that belong to the aggregate (state transitions, limits, invariants). Put them in the aggregate and test them there.</description></item>
///   <item><description>Call <c>SaveChangesAsync</c> or open transactions. The transaction pipeline behavior saves and commits after the handler returns a successful result.</description></item>
///   <item><description>Modify more than one aggregate. React to changes of other aggregates with domain events.</description></item>
///   <item><description>Publish integration events directly. Raise a domain event in the aggregate and translate it in an <see cref="IDomainEventHandler{TEvent}"/>.</description></item>
///   <item><description>Throw for expected failures. Return an <see cref="Error"/> instead.</description></item>
/// </list>
/// <para>
/// Declare handlers as <c>internal sealed</c> classes named <c>{UseCase}Handler</c> in the same folder as the command.
/// They are discovered automatically by <c>AddAppApplication</c>; no manual registration is needed.
/// Unit-test them with fake repositories (see the <c>Fakes</c> in <c>*.Application.Tests</c>).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// internal sealed class CreateCategoryHandler(ICategoryRepository categories) : ICommandHandler&lt;CreateCategory, Result&lt;Guid&gt;&gt;
/// {
///     public async Task&lt;Result&lt;Guid&gt;&gt; Handle(CreateCategory command, CancellationToken cancellationToken)
///     {
///         if (await categories.SlugExistsAsync(command.Slug, cancellationToken))
///         {
///             return CategoryErrors.SlugTaken;
///         }
///
///         if (!Category.Create(command.Name, command.Slug).TryGetValue(out var category, out var error))
///         {
///             return error;
///         }
///
///         categories.Add(category);
///         return category.Id.Value;
///     }
/// }
/// </code>
/// </example>
/// <typeparam name="TCommand">The command type handled by this class.</typeparam>
/// <typeparam name="TResponse">The result type declared by the command, typically <see cref="Result{T}"/>.</typeparam>
/// <seealso cref="ICommand{TResponse}"/>
/// <seealso cref="ICommandHandler{TCommand}"/>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
    where TResponse : IResultFactory<TResponse>;
