using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;

namespace SuperApp.Framework.Application.Messaging;

/// <summary>
/// A request to change the state of the system that reports only success or failure, without returning data.
/// Shorthand for <see cref="ICommand{TResponse}"/> with <see cref="Result"/> as the response.
/// </summary>
/// <remarks>
/// <para>
/// Most state changes are of this kind: renaming, publishing, archiving, deleting. The caller learns whether the change was applied;
/// if it needs the new state, it sends a query afterwards. The command runs through the same pipeline as every command
/// (logging, authorization, validation, transaction); see <see cref="ICommand{TResponse}"/> for the full description and conventions.
/// </para>
/// <para>
/// Handle it with an <see cref="ICommandHandler{TCommand}"/>. In a controller, <c>this.ToActionResult(result)</c> turns a successful
/// result into HTTP 204 No Content and a failure into <c>ProblemDetails</c>.
/// </para>
/// <para>
/// Commands are also sent by message consumers in the Worker. There the current user is the system identity, which has every scope,
/// so <see cref="RequiresScopeAttribute"/> does not block internal processing.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [RequiresScope(KnowledgeScopes.CatalogWrite)]
/// public sealed record PublishMaterial(Guid MaterialId) : ICommand;
///
/// internal sealed class PublishMaterialHandler(IMaterialRepository materials, IClock clock) : ICommandHandler&lt;PublishMaterial&gt;
/// {
///     public async Task&lt;Result&gt; Handle(PublishMaterial command, CancellationToken cancellationToken)
///     {
///         if (!MaterialId.Create(command.MaterialId).TryGetValue(out var materialId, out _))
///         {
///             return MaterialErrors.NotFound;
///         }
///
///         var material = await materials.GetAsync(materialId, cancellationToken);
///         return material is null ? MaterialErrors.NotFound : material.Publish(clock.UtcNow);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="ICommand{TResponse}"/>
/// <seealso cref="ICommandHandler{TCommand}"/>
public interface ICommand : ICommand<Result>;
