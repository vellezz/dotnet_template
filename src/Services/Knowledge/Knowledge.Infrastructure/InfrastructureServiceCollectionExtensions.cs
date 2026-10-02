using Knowledge.Domain.Library.Completions;
using Knowledge.Domain.Library.Favorites;
using Knowledge.Infrastructure.Persistence.Write.Repositories;
using SuperApp.Framework.Application;
using SuperApp.Framework.Infrastructure.Analytics;
using SuperApp.Framework.Infrastructure.Caching;
using SuperApp.Framework.Infrastructure.Messaging;
using SuperApp.Framework.Infrastructure.Persistence;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Knowledge.Application;
using Knowledge.Domain.Categories;
using Knowledge.Domain.Collections;
using Knowledge.Domain.Materials;
using Knowledge.Infrastructure.Persistence.Read;
using Knowledge.Infrastructure.Persistence.Write;

namespace Knowledge.Infrastructure;

/// <summary>
/// Composition root of the Knowledge service: registers the Application layer, persistence, caching, repositories and messaging in the
/// DI container of <c>Knowledge.Api</c> and <c>Knowledge.Worker</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place where Api and Worker touch Infrastructure (ADR-0002): they call <see cref="AddKnowledgeInfrastructure"/> once
/// in <c>Program.cs</c> and otherwise depend only on Application. Integration tests call <see cref="AddKnowledgeCore"/>, which leaves out
/// MassTransit so that tests can replace <c>IIntegrationEventPublisher</c> with a fake.
/// </para>
/// <para>
/// When you add an aggregate, register its repository in <see cref="AddKnowledgeCore"/>. Handlers, validators and domain event handlers
/// need no registration; they are discovered in the Application and Infrastructure assemblies.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Knowledge.Worker/Program.cs
/// builder.Services.AddKnowledgeInfrastructure(
///     builder.Configuration,
///     OutboxDelivery.Enabled,
///     bus =&gt; bus.AddConsumers(typeof(Program).Assembly));
/// </code>
/// </example>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the Knowledge service needs at runtime; call it exactly once from <c>Program.cs</c> of the Api or the Worker.
    /// </summary>
    /// <remarks>
    /// Registers everything from <see cref="AddKnowledgeCore"/> plus MassTransit over RabbitMQ with the Entity Framework outbox and inbox
    /// on <see cref="KnowledgeWriteDbContext"/>. Integration events published by command handlers are written to the outbox in the same
    /// transaction as the aggregate and delivered by the process that has delivery enabled. Receive endpoints are named in kebab-case with
    /// the prefix <see cref="KnowledgeWriteDbContext.SchemaName"/>, use quorum queues and retry failed messages before moving them to
    /// <c>_error</c>.
    /// </remarks>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">
    /// Application configuration: connection strings <c>Write</c>, <c>Read</c>, <c>RabbitMq</c> and optionally <c>Redis</c>; optional sections
    /// <c>Analytics</c> and <c>FeatureFlags</c> for feature flags (ADR-0036).
    /// </param>
    /// <param name="outboxDelivery">
    /// Whether this process delivers outbox messages to RabbitMQ: <see cref="OutboxDelivery.Disabled"/> in the Api,
    /// <see cref="OutboxDelivery.Enabled"/> in the Worker, so that exactly one kind of process sends them.
    /// </param>
    /// <param name="configureConsumers">
    /// Optional registration of MassTransit consumers (the Worker passes its assembly); <see langword="null"/> in the Api, which has no consumers.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddKnowledgeInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        OutboxDelivery outboxDelivery,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        services.AddKnowledgeCore(configuration);
        services.AddAppMessaging<KnowledgeWriteDbContext>(configuration, KnowledgeWriteDbContext.SchemaName, outboxDelivery, configureConsumers);
        return services;
    }

    /// <summary>
    /// Registers the Application layer (MediatR with pipeline behaviors, validators, domain event handlers), the write and read contexts,
    /// the fail-safe cache and the repositories, without messaging.
    /// </summary>
    /// <remarks>
    /// Called by <see cref="AddKnowledgeInfrastructure"/> and directly by integration tests. Without messaging the host must register its
    /// own <c>IIntegrationEventPublisher</c>, <c>IClock</c> and <c>ICurrentUser</c> (the tests use fakes). Handlers are discovered in
    /// <c>KnowledgeApplication.Assembly</c> and in this assembly (query handlers and cache invalidation handlers live here, ADR-0026).
    /// Without the <c>Redis</c> connection string only the in-memory cache level is used.
    /// </remarks>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">
    /// Application configuration: connection strings <c>Write</c>, <c>Read</c> and optionally <c>Redis</c>; optional sections <c>Analytics</c>
    /// (PostHog, enables flag evaluation in PostHog) and <c>FeatureFlags</c> (flag values when analytics is disabled), ADR-0036.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddKnowledgeCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAppApplication(KnowledgeApplication.Assembly, typeof(InfrastructureServiceCollectionExtensions).Assembly);

        services.AddAppPersistence<KnowledgeWriteDbContext, KnowledgeReadDbContext>(configuration, KnowledgeWriteDbContext.SchemaName);
        services.AddAppCaching(configuration, KnowledgeWriteDbContext.SchemaName);
        services.AddAppFeatureFlags(configuration);

        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IMaterialRepository, MaterialRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<IFavoriteRepository, FavoriteRepository>();
        services.AddScoped<IMaterialCompletionRepository, MaterialCompletionRepository>();

        return services;
    }
}
