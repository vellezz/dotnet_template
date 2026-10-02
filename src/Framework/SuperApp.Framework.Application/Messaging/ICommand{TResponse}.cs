using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using MediatR;

namespace SuperApp.Framework.Application.Messaging;

/// <summary>
/// A request to change the state of the system that returns a value, for example the identifier of a created aggregate.
/// Part of the write side of CQRS.
/// </summary>
/// <remarks>
/// <para>
/// A command is an immutable message describing one use case in the ubiquitous language of the context
/// (<c>CreateCategory</c>, <c>PublishMaterial</c>, <c>RecordSleepEntry</c>). It is sent through MediatR's <c>ISender.Send</c>
/// by a controller (HTTP) or a consumer (message bus) and handled by exactly one <see cref="ICommandHandler{TCommand, TResponse}"/>.
/// </para>
/// <para>Every command passes through the pipeline behaviors registered by <c>AddAppApplication</c>, in this order (ADR-0017):</para>
/// <list type="number">
///   <item><description>Logging and tracing: one span and one log entry per command, including the error code on failure.</description></item>
///   <item><description>Authorization: checks <see cref="RequiresScopeAttribute"/> on the command type against the current user's token.</description></item>
///   <item><description>Validation: runs all FluentValidation validators of the command; failures become <see cref="ErrorType.Validation"/> errors.</description></item>
///   <item><description>Transaction: opens a database transaction, runs the handler and, only when the result is successful, saves all changes
///   (dispatching domain events and writing the outbox) and commits. A failed result or an exception rolls everything back.</description></item>
/// </list>
/// <para>Conventions (ADR-0026):</para>
/// <list type="bullet">
///   <item><description>Place the command in <c>Application/Features/{Aggregate}/{UseCase}/{UseCase}.cs</c> as a <c>public sealed record</c> named after the use case, without a "Command" suffix.</description></item>
///   <item><description>Its handler (<c>{UseCase}Handler</c>) and validator (<c>{UseCase}Validator</c>) live in the same folder and are <c>internal sealed</c>.</description></item>
///   <item><description>Use primitives (<see cref="Guid"/>, <see cref="string"/>) for identifiers and values; the handler converts them to strongly typed IDs and value objects.</description></item>
///   <item><description>One command changes one aggregate. If several aggregates must react, raise a domain event and handle it.</description></item>
///   <item><description>Mark the command with <see cref="RequiresScopeAttribute"/>, or with <see cref="AllowAnonymousRequestAttribute"/> if it is public.</description></item>
/// </list>
/// <para>Use <see cref="ICommand"/> for commands that return nothing but success or failure.</para>
/// </remarks>
/// <example>
/// <code>
/// [RequiresScope(KnowledgeScopes.CatalogWrite)]
/// public sealed record CreateCategory(string Name, string Slug) : ICommand&lt;Result&lt;Guid&gt;&gt;;
///
/// // in a controller
/// var result = await sender.Send(new CreateCategory(request.Name, request.Slug), cancellationToken);
/// return this.ToActionResult(result, id =&gt; StatusCode(StatusCodes.Status201Created, new CreatedResponse(id)));
/// </code>
/// </example>
/// <typeparam name="TResponse">
/// Result type of the command, always <see cref="Result{T}"/> (for example <c>Result&lt;Guid&gt;</c>). The constraint lets pipeline behaviors
/// stop the command with an error before the handler runs.
/// </typeparam>
/// <seealso cref="ICommand"/>
/// <seealso cref="ICommandHandler{TCommand, TResponse}"/>
/// <seealso cref="IQuery{TResponse}"/>
public interface ICommand<TResponse> : IRequest<TResponse>
    where TResponse : IResultFactory<TResponse>;
