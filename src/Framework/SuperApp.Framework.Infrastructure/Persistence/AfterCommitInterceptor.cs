using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SuperApp.Framework.Infrastructure.Persistence;

/// <summary>
/// EF Core transaction interceptor of write contexts that runs the actions registered with <c>IUnitOfWork.OnCommitted</c>
/// once the transaction has committed, and discards them when it rolls back (ADR-0020, ADR-0027).
/// </summary>
/// <remarks>
/// <para>
/// It observes every transaction of the context, whichever component opened it: the transaction pipeline behavior in the API, or the
/// MassTransit EF outbox when a Worker consumer sends a command. That is why after-commit work is tied to EF transactions and not to
/// the pipeline.
/// </para>
/// <para>
/// Actions are registered while domain events are dispatched, which can happen before EF Core opens the implicit transaction of a
/// save outside an explicit transaction; they are therefore not discarded when a transaction starts. Besides rollbacks, they are
/// discarded by the write context when a save fails and when a unit-of-work transaction is disposed without commit.
/// </para>
/// <para>
/// A failing action is logged (event ID 210) and swallowed: the command is already committed and must not be reported as failed.
/// Actions are expected to be best-effort housekeeping such as cache invalidation, where a missed run is limited by entry expiration.
/// </para>
/// </remarks>
/// <param name="logger">Logger for failed after-commit actions.</param>
internal sealed partial class AfterCommitInterceptor(ILogger<AfterCommitInterceptor> logger) : DbTransactionInterceptor
{
    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is WriteDbContextBase context)
        {
            // The commit has happened: run the actions even if the request is being cancelled right now.
            await RunAsync(context, CancellationToken.None);
        }
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is WriteDbContextBase context)
        {
            RunAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Discard(eventData.Context);

    private static void Discard(Microsoft.EntityFrameworkCore.DbContext? context)
    {
        if (context is WriteDbContextBase writeContext)
        {
            _ = writeContext.TakeAfterCommitActions();
        }
    }

    private async Task RunAsync(WriteDbContextBase context, CancellationToken cancellationToken)
    {
        foreach (var action in context.TakeAfterCommitActions())
        {
            try
            {
                await action(cancellationToken);
            }
            catch (Exception exception)
            {
                LogAfterCommitActionFailed(logger, context.GetType().Name, exception);
            }
        }
    }

    [LoggerMessage(210, LogLevel.Warning, "After-commit action of {Context} failed")]
    private static partial void LogAfterCommitActionFailed(ILogger logger, string context, Exception exception);
}
