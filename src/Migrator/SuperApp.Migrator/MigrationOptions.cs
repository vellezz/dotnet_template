using Microsoft.EntityFrameworkCore;

namespace SuperApp.Migrator;

/// <summary>
/// Shared SQL Server options for the service write contexts when they are migrated by the migrator.
/// </summary>
/// <remarks>
/// <para>
/// Each context keeps its own <c>__EFMigrationsHistory</c> table in its own schema (ADR-0021), so services never see
/// each other's migration history. The option must match what the service itself uses at run time, otherwise the
/// startup check for pending migrations (ADR-0018) would read a different history table.
/// </para>
/// <para>
/// The command timeout is raised to 10 minutes because data migrations and index builds can take far longer than
/// the default 30 seconds. The gateway context is configured by its own <c>GatewayDbContextOptions</c> instead.
/// </para>
/// </remarks>
internal static class MigrationOptions
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Configures a write context for SQL Server with the migration history table in the service schema.</summary>
    /// <param name="options">The options builder of the context being registered.</param>
    /// <param name="connectionString">The <c>Migrator</c> connection string (login <c>superapp_migrator</c>, DDL and DML rights; dev/test only).</param>
    /// <param name="schema">The service schema, e.g. <c>KnowledgeWriteDbContext.SchemaName</c>; holds the history table.</param>
    public static void Configure(DbContextOptionsBuilder options, string connectionString, string schema) =>
        options.UseSqlServer(connectionString, sql => sql
            .MigrationsHistoryTable("__EFMigrationsHistory", schema)
            .CommandTimeout((int)CommandTimeout.TotalSeconds));
}
