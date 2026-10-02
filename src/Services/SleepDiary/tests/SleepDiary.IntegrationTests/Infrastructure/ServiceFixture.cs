using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Time;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SleepDiary.Infrastructure;
using SleepDiary.Infrastructure.Persistence.Write;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SleepDiary.IntegrationTests.Infrastructure.ServiceFixture))]

namespace SleepDiary.IntegrationTests.Infrastructure;

/// <summary>
/// MSSQL in a container, migrations of the write context and the MediatR pipeline without the MassTransit bus
/// (integration events are published to a fake, ADR-0005).
/// </summary>
public sealed class ServiceFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public ServiceProvider Services { get; private set; } = null!;

    public TestCurrentUser CurrentUser { get; } = new();

    public TestIntegrationEventPublisher Publisher { get; } = new();

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
        services.AddSleepDiaryCore(configuration);
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SleepDiaryWriteDbContext>().Database;
        if (!database.GetMigrations().Any())
        {
            throw new InvalidOperationException(
                "Serwis nie ma migracji. Utwórz pierwszą: dotnet ef migrations add Initial "
                + "-p src/Services/SleepDiary/SleepDiary.Infrastructure -s src/Services/SleepDiary/SleepDiary.Infrastructure --context SleepDiaryWriteDbContext -o Migrations (ADR-0030).");
        }

        await database.MigrateAsync();
    }

    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _container.DisposeAsync();
    }
}
