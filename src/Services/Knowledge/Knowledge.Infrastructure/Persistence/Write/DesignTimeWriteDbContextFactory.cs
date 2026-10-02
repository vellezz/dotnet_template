using SuperApp.Framework.Infrastructure.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Knowledge.Infrastructure.Persistence.Write;

/// <summary>
/// Creates <see cref="KnowledgeWriteDbContext"/> for the <c>dotnet ef</c> tools at design time, so that migrations can be added without
/// starting the API (ADR-0004).
/// </summary>
/// <remarks>
/// The connection string is a placeholder: adding a migration only needs the model, not a database. The design-time dispatcher throws if
/// anything tries to dispatch domain events. Do not use this factory at runtime, and do not run <c>dotnet ef database update</c> against real databases; migrations are
/// applied by <c>SuperApp.Migrator</c> (dev/test) or by the DBA from an idempotent script (prod).
/// </remarks>
/// <example>
/// <code>
/// dotnet ef migrations add AddMaterialSubtitle -p src/Services/Knowledge/Knowledge.Infrastructure -s src/Services/Knowledge/Knowledge.Infrastructure --context KnowledgeWriteDbContext -o Migrations
/// </code>
/// </example>
internal sealed class DesignTimeWriteDbContextFactory : IDesignTimeDbContextFactory<KnowledgeWriteDbContext>
{
    /// <inheritdoc />
    public KnowledgeWriteDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<KnowledgeWriteDbContext>()
            .UseSqlServer(
                "Server=design-time;Database=SuperApp;Integrated Security=true",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", KnowledgeWriteDbContext.SchemaName))
            .Options;

        return new KnowledgeWriteDbContext(options, DesignTimeDomainEventDispatcher.Instance);
    }
}
