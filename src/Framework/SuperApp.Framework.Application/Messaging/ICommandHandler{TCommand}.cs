using SuperApp.Framework.Domain.Results;

namespace SuperApp.Framework.Application.Messaging;

/// <summary>
/// Handles one <see cref="ICommand"/> type, a command that returns only success or failure (<see cref="Result"/>).
/// </summary>
/// <remarks>
/// Same responsibilities and rules as <see cref="ICommandHandler{TCommand, TResponse}"/>: orchestrate, delegate business logic to the aggregate,
/// never save or commit yourself. The <c>Handle</c> method returns <c>Task&lt;Result&gt;</c>; returning the result of the aggregate method
/// (for example <c>return material.Publish(clock.UtcNow);</c>) is the usual last line.
/// </remarks>
/// <example>
/// <code>
/// internal sealed class RemoveFavoritesOfItemHandler(IFavoriteRepository favorites) : ICommandHandler&lt;RemoveFavoritesOfItem&gt;
/// {
///     public async Task&lt;Result&gt; Handle(RemoveFavoritesOfItem command, CancellationToken cancellationToken)
///     {
///         await favorites.RemoveAllForItemAsync(command.ItemType, command.ItemId, cancellationToken);
///         return Result.Success();
///     }
/// }
/// </code>
/// </example>
/// <typeparam name="TCommand">The command type handled by this class.</typeparam>
/// <seealso cref="ICommand"/>
public interface ICommandHandler<in TCommand> : ICommandHandler<TCommand, Result>
    where TCommand : ICommand;
