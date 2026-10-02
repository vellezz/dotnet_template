using SuperApp.Framework.Infrastructure.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ServiceName.Infrastructure.Persistence.Write;

/// <summary>
/// Creates <see cref="ServiceNameWriteDbContext"/> for the EF Core command-line tools, which run without the application's DI container.
/// </summary>
/// <remarks>
/// <para>
/// Used only by <c>dotnet ef</c> to build the model and generate migrations; it never connects to the placeholder server in the
/// connection string and is never used at run time. The migration history table must stay in the service schema, exactly as
/// configured by <c>AddAppPersistence</c> and <c>SuperApp.Migrator</c>, otherwise generated migrations would not match (ADR-0021).
/// </para>
/// <para>Adding a migration (from the repository root), after every change of the write model (expand/contract, ADR-0004):</para>
/// <code>
/// dotnet ef migrations add &lt;Name&gt; -p src/Services/ServiceName/ServiceName.Infrastructure -s src/Services/ServiceName/ServiceName.Infrastructure --context ServiceNameWriteDbContext -o Migrations
/// </code>
/// <para>Never edit a migration that has already been applied anywhere; add a new one instead.</para>
/// </remarks>
internal sealed class DesignTimeWriteDbContextFactory : IDesignTimeDbContextFactory<ServiceNameWriteDbContext>
{
    /// <summary>Creates the write context with SQL Server options and a dispatcher that throws if events were ever dispatched.</summary>
    /// <param name="args">Arguments passed by <c>dotnet ef</c> after <c>--</c>; ignored.</param>
    /// <returns>A write context usable only for building the model.</returns>
    public ServiceNameWriteDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ServiceNameWriteDbContext>()
            .UseSqlServer(
                "Server=design-time;Database=SuperApp;Integrated Security=true",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ServiceNameWriteDbContext.SchemaName))
            .Options;

        return new ServiceNameWriteDbContext(options, DesignTimeDomainEventDispatcher.Instance);
    }
}
