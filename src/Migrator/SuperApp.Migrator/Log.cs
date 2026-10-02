using Microsoft.Extensions.Logging;

namespace SuperApp.Migrator;

/// <summary>
/// Source-generated log messages of the migrator (event IDs 4001-4003).
/// </summary>
/// <remarks>
/// The log is the only record of how far the Job got: contexts are migrated one by one in a fixed order, so the last
/// <see cref="Migrating"/> entry without a matching <see cref="Migrated"/> entry names the schema that failed (ADR-0004).
/// </remarks>
internal static partial class Log
{
    /// <summary>Logs that migration of a context starts.</summary>
    /// <param name="logger">The migrator logger.</param>
    /// <param name="context">Name of the <c>DbContext</c> type, e.g. <c>KnowledgeWriteDbContext</c>.</param>
    /// <param name="pendingCount">Number of migrations not yet applied to the database; 0 means the context is already up to date.</param>
    [LoggerMessage(4001, LogLevel.Information, "Migrating {Context}: {PendingCount} pending migration(s)")]
    public static partial void Migrating(ILogger logger, string context, int pendingCount);

    /// <summary>Logs that all pending migrations of a context were applied.</summary>
    /// <param name="logger">The migrator logger.</param>
    /// <param name="context">Name of the <c>DbContext</c> type.</param>
    [LoggerMessage(4002, LogLevel.Information, "Migrated {Context}")]
    public static partial void Migrated(ILogger logger, string context);

    /// <summary>Logs a failed migration; the migrator then exits with code 1, which fails the Job and stops the rollout.</summary>
    /// <param name="logger">The migrator logger.</param>
    /// <param name="context">Name of the <c>DbContext</c> type whose migration failed.</param>
    /// <param name="exception">The exception thrown by <c>MigrateAsync</c> (usually a <c>SqlException</c>).</param>
    [LoggerMessage(4003, LogLevel.Error, "Migration of {Context} failed; rollout must not continue")]
    public static partial void Failed(ILogger logger, string context, Exception exception);
}
