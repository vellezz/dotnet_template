using SuperApp.Framework.Application.Events;
using SleepDiary.Infrastructure.Persistence.Write.Repositories;
using SuperApp.Framework.Application;
using SuperApp.Framework.Infrastructure.Analytics;
using SuperApp.Framework.Infrastructure.Caching;
using SuperApp.Framework.Infrastructure.Messaging;
using SuperApp.Framework.Infrastructure.Persistence;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SleepDiary.Application;
using SleepDiary.Domain.Entries;
using SleepDiary.Infrastructure.Persistence.Read;
using SleepDiary.Infrastructure.Persistence.Write;

namespace SleepDiary.Infrastructure;

/// <summary>
/// Composition root of the SleepDiary service: registers the application layer and all infrastructure (persistence, cache, messaging,
/// repositories) in the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place where Infrastructure types are wired to the ports declared in Domain and Application. The Api and Worker hosts call
/// <see cref="AddSleepDiaryInfrastructure"/>; integration tests call <see cref="AddSleepDiaryCore"/> to get everything except the MassTransit bus
/// (they register a fake <see cref="IIntegrationEventPublisher"/> instead).
/// </para>
/// <para>
/// Configuration keys used: <c>ConnectionStrings:Write</c>, <c>ConnectionStrings:Read</c>, optional <c>ConnectionStrings:Redis</c>,
/// and for messaging <c>ConnectionStrings:RabbitMq</c>. Secrets come from Vault /
/// External Secrets, never from <c>appsettings*.json</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Api: writes integration events to the outbox only
/// builder.Services.AddSleepDiaryInfrastructure(builder.Configuration, OutboxDelivery.Disabled);
///
/// // Worker: also delivers the outbox to RabbitMQ and hosts consumers
/// builder.Services.AddSleepDiaryInfrastructure(builder.Configuration, OutboxDelivery.Enabled, bus =&gt; bus.AddConsumers(typeof(Program).Assembly));
/// </code>
/// </example>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything a SleepDiary host needs: all of <see cref="AddSleepDiaryCore"/> plus MassTransit on RabbitMQ with the EF Core
    /// outbox and inbox on <see cref="SleepDiaryWriteDbContext"/>.
    /// </summary>
    /// <remarks>Call it only from the Api and Worker composition roots (ADR-0002).</remarks>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">Host configuration: connection strings <c>Write</c>, <c>Read</c>, <c>RabbitMq</c>, optional <c>Redis</c>; optional sections <c>Analytics</c> and <c>FeatureFlags</c> (ADR-0036).</param>
    /// <param name="outboxDelivery">
    /// Whether this process delivers stored outbox messages to the broker: <see cref="OutboxDelivery.Disabled"/> in the Api (it only writes
    /// to the outbox), <see cref="OutboxDelivery.Enabled"/> in the Worker.
    /// </param>
    /// <param name="configureConsumers">
    /// Optional registration of MassTransit consumers (used by the Worker); <see langword="null"/> when the process consumes no messages.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for call chaining.</returns>
    public static IServiceCollection AddSleepDiaryInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        OutboxDelivery outboxDelivery,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        services.AddSleepDiaryCore(configuration);
        services.AddAppMessaging<SleepDiaryWriteDbContext>(configuration, SleepDiaryWriteDbContext.SchemaName, outboxDelivery, configureConsumers);
        return services;
    }

    /// <summary>
    /// Registers the application layer (MediatR pipeline, handlers, validators, domain event handlers from the Application and Infrastructure
    /// assemblies), both <c>DbContext</c>s, the two-level cache and the repositories, without messaging.
    /// </summary>
    /// <remarks>
    /// Without messaging nothing implements <see cref="IIntegrationEventPublisher"/>; hosts get it from <see cref="AddSleepDiaryInfrastructure"/>,
    /// tests must register their own.
    /// </remarks>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">Host configuration: connection strings <c>Write</c>, <c>Read</c>, optional <c>Redis</c>; optional sections <c>Analytics</c> (PostHog) and <c>FeatureFlags</c> (flag values when analytics is disabled), ADR-0036.</param>
    /// <returns>The same <paramref name="services"/> instance, for call chaining.</returns>
    public static IServiceCollection AddSleepDiaryCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAppApplication(SleepDiaryApplication.Assembly, typeof(InfrastructureServiceCollectionExtensions).Assembly);

        services.AddAppPersistence<SleepDiaryWriteDbContext, SleepDiaryReadDbContext>(configuration, SleepDiaryWriteDbContext.SchemaName);
        services.AddAppCaching(configuration, SleepDiaryWriteDbContext.SchemaName);
        services.AddAppFeatureFlags(configuration);

        services.AddScoped<ISleepEntryRepository, SleepEntryRepository>();

        return services;
    }
}
