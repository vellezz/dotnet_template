using System.Data;
using SuperApp.Gateway.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SuperApp.Gateway.Bff.Sessions;

/// <summary>
/// Background service of the <c>bff-web</c> profile that deletes expired rows from <c>gateway.Sessions</c> every 10 minutes (ADR-0013).
/// </summary>
/// <remarks>
/// <para>
/// Expired sessions are already ignored by <see cref="SuperApp.Gateway.Bff.Sessions.DbTicketStore.RetrieveAsync"/>; this service only keeps the table small.
/// Sessions without an expiry (<see cref="SuperApp.Gateway.Persistence.Entities.Session.ExpiresAt"/> is <see langword="null"/>) are never deleted here.
/// </para>
/// <para>
/// All replicas run the service, but each run takes the exclusive SQL Server application lock <c>gateway-session-cleanup</c>
/// (<c>sp_getapplock</c>, transaction-owned, no wait); a replica that does not get the lock skips the run, so at most one replica cleans up
/// at a time. SQL and invalid-operation errors are logged (event 3302) and the next run is attempted at the next tick.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates a scope per run to resolve a <see cref="SuperApp.Gateway.Persistence.GatewayDbContext"/>.</param>
/// <param name="timeProvider">Clock used to decide which sessions have expired.</param>
/// <param name="logger">Logger for the result of each run.</param>
internal sealed partial class SessionCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<SessionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    /// <summary>Runs the cleanup every <c>Interval</c> (10 minutes, first run after the first interval) until the host stops.</summary>
    /// <param name="stoppingToken">Signalled when the host shuts down.</param>
    /// <returns>A task that completes when the host stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is SqlException or InvalidOperationException)
            {
                LogCleanupFailed(logger, exception);
            }
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // @LockTimeout = 0: do not wait; a negative result means another replica holds the lock (or the call failed).
        var lockResult = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = 'gateway-session-cleanup', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0",
            [lockResult],
            cancellationToken);
        if ((int)lockResult.Value < 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var removed = await db.Sessions.Where(session => session.ExpiresAt != null && session.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogCleanedUp(logger, removed);
    }

    [LoggerMessage(3301, LogLevel.Information, "Removed {Count} expired BFF session(s)")]
    private static partial void LogCleanedUp(ILogger logger, int count);

    [LoggerMessage(3302, LogLevel.Warning, "Expired BFF session cleanup failed")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
