using Microsoft.EntityFrameworkCore;

namespace SuperApp.Gateway.Persistence;

/// <summary>
/// Shared configuration of <see cref="SuperApp.Gateway.Persistence.GatewayDbContext"/>, used by the gateway, the design-time factory, <c>SuperApp.Migrator</c> and the tests,
/// so all of them agree on the provider and on where the migration history lives (ADR-0004, ADR-0021).
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddDbContext&lt;GatewayDbContext&gt;(options =&gt;
///     GatewayDbContextOptions.Configure(options, builder.Configuration.GetConnectionString("Gateway")));
/// </code>
/// </example>
public static class GatewayDbContextOptions
{
    /// <summary>
    /// Configures the SQL Server provider and stores the migration history in <c>gateway.__EFMigrationsHistory</c>
    /// (schema <see cref="SuperApp.Gateway.Persistence.GatewayDbContext.SchemaName"/>). That table is also the version source of the route configuration (ADR-0022).
    /// </summary>
    /// <param name="options">Options builder of the context being configured.</param>
    /// <param name="connectionString">
    /// Connection string of the database; <see langword="null"/> does not fail here, a missing connection only surfaces on the first use of the context.
    /// </param>
    public static void Configure(DbContextOptionsBuilder options, string? connectionString) =>
        options.UseSqlServer(
            connectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", GatewayDbContext.SchemaName)
                .ExecutionStrategy(dependencies => new SuperApp.Framework.Infrastructure.Persistence.AppSqlExecutionStrategy(dependencies)));
}
