using SleepDiary.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SleepDiary.Infrastructure.Persistence.Write;

namespace SleepDiary.IntegrationTests;

public sealed class SchemaTests(ServiceFixture fixture)
{
    /// <summary>Migrations create tables only in the service schema, including the outbox and the migrations history (ADR-0004, ADR-0021).</summary>
    [Fact]
    public async Task Migrations_create_tables_in_service_schema()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SleepDiaryWriteDbContext>();

        var tables = await db.Database
            .SqlQuery<string>($"SELECT TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = {SleepDiaryWriteDbContext.SchemaName}")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains("__EFMigrationsHistory", tables);
        Assert.Contains("OutboxMessage", tables);
        Assert.Contains("InboxState", tables);
    }
}
