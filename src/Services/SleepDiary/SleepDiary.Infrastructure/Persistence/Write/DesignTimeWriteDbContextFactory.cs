using SuperApp.Framework.Infrastructure.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SleepDiary.Infrastructure.Persistence.Write;

/// <summary>
/// Creates <see cref="SleepDiaryWriteDbContext"/> for the <c>dotnet ef</c> tools, without starting a host (ADR-0004).
/// </summary>
/// <remarks>
/// <para>
/// Used only at design time to add migrations or generate scripts; it never connects to a database, so the connection string is a placeholder.
/// Domain events are not dispatched (<c>DesignTimeDomainEventDispatcher</c>). Migrations are applied by <c>SuperApp.Migrator</c> (dev/test) or by the
/// DBA from an idempotent script (prod), never by the service at startup.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// dotnet ef migrations add AddSleepEntryTags -p src/Services/SleepDiary/SleepDiary.Infrastructure -s src/Services/SleepDiary/SleepDiary.Infrastructure --context SleepDiaryWriteDbContext -o Migrations
/// </code>
/// </example>
internal sealed class DesignTimeWriteDbContextFactory : IDesignTimeDbContextFactory<SleepDiaryWriteDbContext>
{
    /// <inheritdoc />
    public SleepDiaryWriteDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SleepDiaryWriteDbContext>()
            .UseSqlServer(
                "Server=design-time;Database=SuperApp;Integrated Security=true",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SleepDiaryWriteDbContext.SchemaName))
            .Options;

        return new SleepDiaryWriteDbContext(options, DesignTimeDomainEventDispatcher.Instance);
    }
}
