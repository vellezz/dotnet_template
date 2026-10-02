using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Infrastructure.Events;
using SuperApp.Gateway.Persistence;
using SuperApp.Migrator;
using Knowledge.Infrastructure.Persistence.Write;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SleepDiary.Infrastructure.Persistence.Write;

// SuperApp.Migrator: applies the EF Core migrations of all write contexts of the application (ADR-0004).
//
// When it runs:
// - dev/test: as the Kubernetes Job superapp-migrator (ArgoCD PreSync hook / sync-wave) BEFORE the services are rolled out;
//   the rollout starts only when the Job succeeds.
// - prod: never. The release pipeline generates `dotnet ef migrations script --idempotent` per context and the DBA
//   runs the combined script before the deployment. Neither the Job nor the superapp_migrator login exist on prod.
//
// What it does: registers every write context (gateway, then each service), and for each one, in this fixed order,
// applies the pending migrations. Already applied migrations are skipped, so re-running the Job is safe. The first
// failure logs the context, exits with code 1 and stops the whole rollout. APIs, Workers and the gateway never migrate;
// they only check at startup that nothing is pending (ADR-0018).
//
// The migrator contains no logic of its own. To add a service: reference its Infrastructure project in the csproj,
// register its WriteDbContext below with MigrationOptions.Configure and append its type to the `contexts` array.
var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Migrator")
    ?? throw new InvalidOperationException("Brak connection stringu Migrator.");

// Service write contexts require an IDomainEventDispatcher in their constructor. Migrations never save aggregates,
// so the design-time dispatcher (it throws if it is ever called) satisfies the dependency.
builder.Services.AddSingleton<IDomainEventDispatcher>(DesignTimeDomainEventDispatcher.Instance);
builder.Services.AddDbContext<GatewayDbContext>(options => GatewayDbContextOptions.Configure(options, connectionString));
builder.Services.AddDbContext<KnowledgeWriteDbContext>(options => MigrationOptions.Configure(options, connectionString, KnowledgeWriteDbContext.SchemaName));
builder.Services.AddDbContext<SleepDiaryWriteDbContext>(options => MigrationOptions.Configure(options, connectionString, SleepDiaryWriteDbContext.SchemaName));

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SuperApp.Migrator");

// Order matters only for readability of the logs and for where the Job stops on failure; schemas are independent (ADR-0021).
Type[] contexts = [typeof(GatewayDbContext), typeof(KnowledgeWriteDbContext), typeof(SleepDiaryWriteDbContext)];
foreach (var contextType in contexts)
{
    await using var scope = host.Services.CreateAsyncScope();
    var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
    var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();

    Log.Migrating(logger, contextType.Name, pending.Count);
    try
    {
        await context.Database.MigrateAsync();
    }
    catch (Exception exception)
    {
        Log.Failed(logger, contextType.Name, exception);
        return 1;
    }

    Log.Migrated(logger, contextType.Name);
}

return 0;
