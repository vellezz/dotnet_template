using SuperApp.Framework.Application.Persistence;
using SuperApp.Framework.Domain.Results;

namespace Knowledge.Application.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IUnitOfWork"/> that counts saves, commits and discards, can simulate a failed save (e.g. a unique index conflict)
/// or a transaction opened by a consumer, and runs after-commit actions on commit, like the real write context.
/// </summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly List<Func<CancellationToken, Task>> _afterCommit = [];

    public int SaveCount { get; private set; }

    public int CommitCount { get; private set; }

    public int AfterCommitRuns { get; private set; }

    public Result SaveResult { get; set; } = Result.Success();

    public int DiscardCount { get; private set; }

    /// <summary>Set to <see langword="true"/> to simulate a command running inside a MassTransit consumer's transaction.</summary>
    public bool HasActiveTransaction { get; set; }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        HasActiveTransaction = true;
        return Task.FromResult<IUnitOfWorkTransaction>(new Transaction(this));
    }

    public Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.FromResult(SaveResult);
    }

    public void OnCommitted(Func<CancellationToken, Task> action) => _afterCommit.Add(action);

    public void DiscardChanges()
    {
        DiscardCount++;
        _afterCommit.Clear();
    }

    private sealed class Transaction(FakeUnitOfWork owner) : IUnitOfWorkTransaction
    {
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            owner.CommitCount++;
            foreach (var action in owner._afterCommit)
            {
                await action(cancellationToken);
                owner.AfterCommitRuns++;
            }

            owner._afterCommit.Clear();
        }

        public ValueTask DisposeAsync()
        {
            owner.HasActiveTransaction = false;
            owner._afterCommit.Clear();
            return ValueTask.CompletedTask;
        }
    }
}
