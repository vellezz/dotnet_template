using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Infrastructure.OpenApi;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SuperApp.Framework.Infrastructure.Messaging;

/// <summary>
/// Registers asynchronous messaging of a service: MassTransit over RabbitMQ with the Entity Framework transactional outbox and inbox (ADR-0005).
/// </summary>
/// <remarks>
/// Called from the service's Infrastructure composition root (<c>Add{Service}Infrastructure</c>), never from Application or Domain code,
/// which publish only through <see cref="IIntegrationEventPublisher"/>. See <see cref="AddAppMessaging{TWriteDbContext}"/> for the conventions.
/// </remarks>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers MassTransit with RabbitMQ, the EF Core outbox and inbox stored in the service's write database, <see cref="IIntegrationEventPublisher"/>
    /// and the standard endpoint conventions (ADR-0005).
    /// </summary>
    /// <remarks>
    /// <para>What this gives every service:</para>
    /// <list type="bullet">
    ///   <item><description><b>Transactional outbox:</b> messages published during a command are stored in the service schema in the same transaction and delivered
    ///   after commit by the process with <see cref="OutboxDelivery.Enabled"/> (the Worker). A crash can never lose or invent a message.</description></item>
    ///   <item><description><b>Inbox on consumers:</b> each received message is recorded, so a redelivered message is processed only once;
    ///   a consumer's command and its outgoing messages share one transaction.</description></item>
    ///   <item><description><b>Retry:</b> consumers retry after 100 ms, 500 ms, 1 s and 5 s; afterwards the message goes to the <c>_error</c> queue, which is monitored.</description></item>
    ///   <item><description><b>Quorum queues</b> and kebab-case queue names prefixed with <paramref name="servicePrefix"/>
    ///   (e.g. <c>knowledge-material-archived</c>).</description></item>
    /// </list>
    /// <para>
    /// The connection string <c>ConnectionStrings:RabbitMq</c> (<c>amqps://user:password@host/vhost</c>) comes from Vault.
    /// MassTransit is used in its last open-source major version 8 (Apache-2.0, ADR-0035); no license key is needed. When the build-time OpenAPI generator runs the application, only <see cref="IIntegrationEventPublisher"/> is registered, so no broker is needed (ADR-0009).
    /// </para>
    /// <para>Consumers belong to the Worker project, are thin, and translate the message into a MediatR command:</para>
    /// <code>
    /// builder.Services.AddKnowledgeInfrastructure(
    ///     builder.Configuration,
    ///     OutboxDelivery.Enabled,
    ///     bus =&gt; bus.AddConsumers(typeof(Program).Assembly));
    /// </code>
    /// </remarks>
    /// <typeparam name="TWriteDbContext">The service's write database context, which contains the outbox and inbox tables.</typeparam>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">Application configuration with the <c>RabbitMq</c> connection string.</param>
    /// <param name="servicePrefix">Prefix of queue names, the service schema name (for example <c>knowledge</c>), keeping queues of services apart.</param>
    /// <param name="outboxDelivery">Whether this process delivers stored outbox messages to RabbitMQ; see <see cref="OutboxDelivery"/>.</param>
    /// <param name="configureConsumers">Registers consumers and sagas (Worker); <see langword="null"/> in the API, which only publishes.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The <c>RabbitMq</c> connection string is missing or empty; thrown when the bus is configured at startup.</exception>
    public static IServiceCollection AddAppMessaging<TWriteDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string servicePrefix,
        OutboxDelivery outboxDelivery,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
        where TWriteDbContext : DbContext
    {
        services.AddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();

        // The build-time OpenAPI generator starts the application without any infrastructure (ADR-0009).
        if (BuildTimeDocumentGeneration.IsActive)
        {
            return services;
        }

        services.AddMassTransit(bus =>
        {
            bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(servicePrefix, includeNamespace: false));
            configureConsumers?.Invoke(bus);

            bus.AddEntityFrameworkOutbox<TWriteDbContext>(outbox =>
            {
                outbox.UseSqlServer();
                outbox.UseBusOutbox(busOutbox =>
                {
                    if (outboxDelivery == OutboxDelivery.Disabled)
                    {
                        busOutbox.DisableDeliveryService();
                    }
                });
            });

            bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                endpoint.UseMessageRetry(retry => retry.Intervals(100, 500, 1000, 5000));
                endpoint.UseEntityFrameworkOutbox<TWriteDbContext>(context);

                if (endpoint is IRabbitMqReceiveEndpointConfigurator rabbitMq)
                {
                    rabbitMq.SetQuorumQueue();
                }
            });

            bus.UsingRabbitMq((context, rabbitMq) =>
            {
                var connectionString = configuration.GetConnectionString("RabbitMq");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException("Brak connection stringu RabbitMq.");
                }

                rabbitMq.Host(new Uri(connectionString));
                rabbitMq.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    private sealed class IntegrationEventPublisher(IPublishEndpoint publishEndpoint) : IIntegrationEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : class =>
            publishEndpoint.Publish(integrationEvent, cancellationToken);
    }
}
