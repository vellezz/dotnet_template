using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SuperApp.Framework.Infrastructure.Persistence;

/// <summary>
/// SQL Server retrying execution strategy that retries transient errors on queries and saves while allowing user-initiated transactions.
/// </summary>
/// <remarks>
/// <para>
/// Standard <see cref="SqlServerRetryingExecutionStrategy"/> forbids user-initiated transactions by throwing
/// <see cref="InvalidOperationException"/> when an operation is executed while a transaction is active
/// without being wrapped in an explicit <c>CreateExecutionStrategy().ExecuteAsync</c> delegate.
/// </para>
/// <para>
/// This strategy retries transient failures for all standard queries and saves, but bypasses retries within an unmanaged ambient transaction,
/// letting transient errors bubble up so that the surrounding transaction can roll back cleanly instead of failing on startup.
/// </para>
/// </remarks>
public sealed class AppSqlExecutionStrategy : SqlServerRetryingExecutionStrategy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppSqlExecutionStrategy"/> class.
    /// </summary>
    /// <param name="dependencies">Execution strategy dependencies.</param>
    public AppSqlExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSqlExecutionStrategy"/> class with retry parameters.
    /// </summary>
    /// <param name="dependencies">Execution strategy dependencies.</param>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    /// <param name="maxRetryDelay">The maximum delay between retries.</param>
    /// <param name="errorNumbersToAdd">Additional SQL error numbers that should be considered transient.</param>
    public AppSqlExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        int maxRetryCount,
        TimeSpan maxRetryDelay,
        ICollection<int>? errorNumbersToAdd)
        : base(dependencies, maxRetryCount, maxRetryDelay, errorNumbersToAdd)
    {
    }

    /// <inheritdoc />
    public override async Task<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken = default)
    {
        if (Dependencies.CurrentContext.Context.Database.CurrentTransaction is not null)
        {
            return await operation(Dependencies.CurrentContext.Context, state, cancellationToken);
        }

        return await base.ExecuteAsync(state, operation, verifySucceeded, cancellationToken);
    }

    /// <inheritdoc />
    public override TResult Execute<TState, TResult>(
        TState state,
        Func<DbContext, TState, TResult> operation,
        Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
    {
        if (Dependencies.CurrentContext.Context.Database.CurrentTransaction is not null)
        {
            return operation(Dependencies.CurrentContext.Context, state);
        }

        return base.Execute(state, operation, verifySucceeded);
    }
}
