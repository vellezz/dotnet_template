using SuperApp.Framework.Application;
using SuperApp.Framework.Infrastructure.Analytics;
using SuperApp.Framework.Infrastructure.Caching;
using SuperApp.Framework.Infrastructure.Messaging;
using SuperApp.Framework.Infrastructure.Persistence;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceName.Application;
using ServiceName.Infrastructure.Persistence.Read;
using ServiceName.Infrastructure.Persistence.Write;

namespace ServiceName.Infrastructure;

/// <summary>
/// Registers the ServiceName service in the DI container: the single entry point the Api and Worker use to wire Application and
/// Infrastructure (composition root, ADR-0002).
/// </summary>
/// <remarks>
/// <para>
/// Api and Worker call <see cref="AddServiceNameInfrastructure"/>; integration tests call <see cref="AddServiceNameCore"/>, which leaves
/// out RabbitMQ so that tests can register a fake <c>IIntegrationEventPublisher</c>.
/// </para>
/// <para>
/// TODO when the service grows: register here, in <see cref="AddServiceNameCore"/>, every implementation of a port defined in Domain or
/// Application: repositories (<c>services.AddScoped&lt;IThingRepository, ThingRepository&gt;()</c>), anti-corruption gateways with their
/// Refit clients (ADR-0014) and other adapters. Command, query and domain event handlers and validators are found by assembly scanning
/// and need no registration.
/// </para>
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registers everything the service needs at run time, including MassTransit messaging; called only from Api and Worker <c>Program.cs</c>.</summary>
    /// <remarks>
    /// Registers everything from <see cref="AddServiceNameCore"/> plus MassTransit over RabbitMQ with the EF outbox and inbox on
    /// <see cref="ServiceNameWriteDbContext"/> (ADR-0005). Queue names are prefixed with <see cref="ServiceNameWriteDbContext.SchemaName"/>.
    /// </remarks>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">
    /// Application configuration: connection strings <c>Write</c>, <c>Read</c>, <c>RabbitMq</c> and optional <c>Redis</c>
    /// (values come from Vault, never from appsettings).
    /// </param>
    /// <param name="outboxDelivery">
    /// Whether this process sends stored outbox messages to RabbitMQ: <see cref="OutboxDelivery.Disabled"/> in the Api,
    /// <see cref="OutboxDelivery.Enabled"/> in the Worker.
    /// </param>
    /// <param name="configureConsumers">Registers the MassTransit consumers (Worker); <see langword="null"/> in the Api, which consumes nothing.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddServiceNameInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        OutboxDelivery outboxDelivery,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        services.AddServiceNameCore(configuration);
        services.AddAppMessaging<ServiceNameWriteDbContext>(configuration, ServiceNameWriteDbContext.SchemaName, outboxDelivery, configureConsumers);
        return services;
    }

    /// <summary>
    /// Registers the Application pipeline, persistence, cache and the service's own adapters, without messaging; used by
    /// <see cref="AddServiceNameInfrastructure"/> and directly by integration tests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AddAppApplication</c> scans the Application assembly and this assembly (query handlers live here, ADR-0026) for handlers,
    /// validators and domain event handlers, and adds the pipeline behaviors in the order of ADR-0017. <c>AddAppPersistence</c> registers
    /// both database contexts and <c>IUnitOfWork</c>; <c>AddAppCaching</c> registers <c>HybridCache</c>, with Redis as L2 only when the
    /// <c>Redis</c> connection string is set (ADR-0020); <c>AddAppFeatureFlags</c> registers <c>IFeatureFlags</c> (PostHog when
    /// <c>Analytics:ProjectToken</c> is set, configuration otherwise, ADR-0036).
    /// </para>
    /// <para>The caller must provide <c>IIntegrationEventPublisher</c> (MassTransit in production, a fake in tests).</para>
    /// </remarks>
    /// <param name="services">The service collection of the host or test.</param>
    /// <param name="configuration">
    /// Application configuration: connection strings <c>Write</c>, <c>Read</c> and optional <c>Redis</c>.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddServiceNameCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAppApplication(ServiceNameApplication.Assembly, typeof(InfrastructureServiceCollectionExtensions).Assembly);

        services.AddAppPersistence<ServiceNameWriteDbContext, ServiceNameReadDbContext>(configuration, ServiceNameWriteDbContext.SchemaName);
        services.AddAppCaching(configuration, ServiceNameWriteDbContext.SchemaName);
        services.AddAppFeatureFlags(configuration);

        return services;
    }
}
