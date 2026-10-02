using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Application.Persistence;
using SuperApp.Framework.Infrastructure.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SuperApp.Framework.Infrastructure.Persistence;

/// <summary>Registers the database contexts of a service according to CQRS: one context for writes, one for reads (ADR-0003).</summary>
/// <remarks>Called from the service's Infrastructure composition root (<c>Add{Service}Core</c>); see <see cref="AddAppPersistence{TWrite, TRead}"/>.</remarks>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the service's write context (also as <see cref="IUnitOfWork"/>), its read context without change tracking,
    /// and the domain event dispatcher.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two contexts use separate connection strings, <c>ConnectionStrings:Write</c> and <c>ConnectionStrings:Read</c>, both with the service's own
    /// database user that can access only the service schema (ADR-0021). The read connection may point to a read-only replica
    /// (<c>ApplicationIntent=ReadOnly</c>) where the database team provides one.
    /// </para>
    /// <para>The migrations history table is kept in the service schema, so every service versions its schema independently.</para>
    /// <para>The write context gets the <see cref="AfterCommitInterceptor"/>, which runs the actions registered with <c>IUnitOfWork.OnCommitted</c>.</para>
    /// </remarks>
    /// <typeparam name="TWrite">The service's write context; the only owner of the schema and of the migrations.</typeparam>
    /// <typeparam name="TRead">The service's read context; maps flat read models, never domain entities.</typeparam>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">Application configuration with the <c>Write</c> and <c>Read</c> connection strings.</param>
    /// <param name="schema">The service schema (e.g. <c>knowledge</c>) that holds <c>__EFMigrationsHistory</c>.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddAppPersistence<TWrite, TRead>(
        this IServiceCollection services,
        IConfiguration configuration,
        string schema)
        where TWrite : WriteDbContextBase
        where TRead : ReadDbContextBase
    {
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        services.AddSingleton<AfterCommitInterceptor>();
        services.AddDbContext<TWrite>((provider, options) => options
            .UseSqlServer(
                configuration.GetConnectionString("Write"),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .AddInterceptors(provider.GetRequiredService<AfterCommitInterceptor>()));

        services.AddDbContext<TRead>(options => options
            .UseSqlServer(configuration.GetConnectionString("Read"))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<TWrite>());
        return services;
    }
}
