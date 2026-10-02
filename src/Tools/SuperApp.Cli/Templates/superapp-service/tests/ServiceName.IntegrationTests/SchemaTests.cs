using ServiceName.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceName.Infrastructure.Persistence.Write;

namespace ServiceName.IntegrationTests;

/// <summary>
/// Checks the database schema created by the service's migrations.
/// </summary>
/// <remarks>
/// Put repository and query handler tests next to this class: send commands and queries with <see cref="ServiceFixture.SendAsync{TResponse}"/> or
/// resolve repositories from <see cref="ServiceFixture.Services"/> in a new scope, against the real MSSQL schema.
/// </remarks>
/// <param name="fixture">The assembly-wide fixture with the migrated database and the DI container.</param>
public sealed class SchemaTests(ServiceFixture fixture)
{
    /// <summary>
    /// Migrations create the migration history, outbox and inbox tables in the service schema (ADR-0004, ADR-0021). Fails until the
    /// initial migration exists.
    /// </summary>
    [Fact]
    public async Task Migrations_create_tables_in_service_schema()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceNameWriteDbContext>();

        var tables = await db.Database
            .SqlQuery<string>($"SELECT TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = {ServiceNameWriteDbContext.SchemaName}")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains("__EFMigrationsHistory", tables);
        Assert.Contains("OutboxMessage", tables);
        Assert.Contains("InboxState", tables);
    }
}
