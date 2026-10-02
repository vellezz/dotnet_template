namespace SuperApp.Framework.Application.Persistence;

/// <summary>
/// A database transaction opened by <see cref="IUnitOfWork.BeginTransactionAsync"/>. Disposing it without calling
/// <see cref="CommitAsync"/> rolls back every change made within it.
/// </summary>
/// <remarks>
/// Used by the transaction pipeline behavior with <c>await using</c>, which guarantees rollback when the handler returns a failure
/// or throws. Application code does not need to use it.
/// </remarks>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    /// <summary>Commits the transaction, making all saved changes and outbox messages durable.</summary>
    /// <param name="cancellationToken">Cancellation of the surrounding request or message.</param>
    /// <returns>A task that completes when the database has committed the transaction.</returns>
    Task CommitAsync(CancellationToken cancellationToken);
}
