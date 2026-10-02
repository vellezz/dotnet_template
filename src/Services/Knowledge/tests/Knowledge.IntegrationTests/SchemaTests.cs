using Knowledge.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Knowledge.Infrastructure.Persistence.Write;

namespace Knowledge.IntegrationTests;

public sealed class SchemaTests(ServiceFixture fixture)
{
    /// <summary>Migrations create tables only in the service schema, together with the outbox and the migrations history (ADR-0004, ADR-0021).</summary>
    [Fact]
    public async Task Migrations_create_tables_in_service_schema()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeWriteDbContext>();

        var tables = await db.Database
            .SqlQuery<string>($"SELECT TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = {KnowledgeWriteDbContext.SchemaName}")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains("__EFMigrationsHistory", tables);
        Assert.Contains("OutboxMessage", tables);
        Assert.Contains("InboxState", tables);
    }
}
