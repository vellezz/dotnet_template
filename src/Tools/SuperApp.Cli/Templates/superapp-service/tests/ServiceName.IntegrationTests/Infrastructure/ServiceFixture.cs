using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Time;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceName.Infrastructure;
using ServiceName.Infrastructure.Persistence.Write;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(ServiceName.IntegrationTests.Infrastructure.ServiceFixture))]

namespace ServiceName.IntegrationTests.Infrastructure;

/// <summary>
/// Assembly-wide test fixture: MSSQL in a Testcontainers container, the migrated write schema and the service's DI container with the
/// full MediatR pipeline but without the MassTransit bus (integration events are recorded by a fake, ADR-0005).
/// </summary>
/// <remarks>
/// <para>
/// Registered with <c>[assembly: AssemblyFixture]</c>, so one container is started for the whole test assembly (Docker is required) and
/// injected into test class constructors. Tests share the database: use new IDs per test and do not rely on table counts.
/// </para>
/// <para>
/// Startup applies the migrations of <c>ServiceNameWriteDbContext</c> exactly as <c>SuperApp.Migrator</c> does, and fails with an explicit
/// message while the service has no migration yet: create the initial migration right after generating the service (ADR-0030).
/// </para>
/// </remarks>
public sealed class ServiceFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    /// <summary>Gets the root DI container of the service; create a scope per unit of work (<c>CreateAsyncScope</c>), scopes are validated.</summary>
    public ServiceProvider Services { get; private set; } = null!;

    /// <summary>Gets the user every request runs as; set its subject and scopes at the start of each test.</summary>
    public TestCurrentUser CurrentUser { get; } = new();

    /// <summary>Gets the recorder of integration events published by domain event handlers.</summary>
    public TestIntegrationEventPublisher Publisher { get; } = new();

    /// <summary>Starts the container, builds the DI container on its connection string and applies the migrations.</summary>
    /// <returns>A task that completes when the database is ready.</returns>
    /// <exception cref="InvalidOperationException">The service has no migration yet.</exception>
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var connectionString = _container.GetConnectionString();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Write"] = connectionString,
                ["ConnectionStrings:Read"] = connectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock>(new TestClock());
        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.AddSingleton<IIntegrationEventPublisher>(Publisher);
        services.AddServiceNameCore(configuration);
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ServiceNameWriteDbContext>().Database;
        if (!database.GetMigrations().Any())
        {
            throw new InvalidOperationException(
                "Serwis nie ma migracji. Utwórz pierwszą: dotnet ef migrations add Initial "
                + "-p src/Services/ServiceName/ServiceName.Infrastructure -s src/Services/ServiceName/ServiceName.Infrastructure --context ServiceNameWriteDbContext -o Migrations (ADR-0030).");
        }

        await database.MigrateAsync();
    }

    /// <summary>Sends a command or query through the full MediatR pipeline (authorization, validation, transaction) in a new DI scope.</summary>
    /// <typeparam name="TResponse">The response type, usually <c>Result</c> or <c>Result&lt;T&gt;</c>.</typeparam>
    /// <param name="request">The command or query; it runs as <see cref="CurrentUser"/>.</param>
    /// <returns>The response of the handler, e.g. a failed <c>Result</c> when authorization or validation rejects the request.</returns>
    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Disposes the DI container and removes the database container.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _container.DisposeAsync();
    }
}
