using SuperApp.Framework.Infrastructure.OpenApi;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SuperApp.Framework.Infrastructure.Messaging;

/// <summary>
/// Registers messaging for a process that only consumes integration events and has no database of its own, such as the analytics
/// forwarder (ADR-0036).
/// </summary>
/// <remarks>
/// <para>
/// Domain services use <see cref="MessagingServiceCollectionExtensions.AddAppMessaging{TWriteDbContext}"/>, whose EF inbox makes each message
/// processed exactly once together with the command it triggers. A subscriber registered here has no inbox: a message redelivered after a
/// crash between processing and acknowledgement is processed again. Use it only for consumers whose effect may repeat harmlessly (sending a
/// product analytics event), never for consumers that change state.
/// </para>
/// <para>
/// Retry and queue conventions are the same as for services: retries after 100 ms, 500 ms, 1 s and 5 s, then the <c>_error</c> queue; quorum
/// queues; kebab-case queue names with <c>subscriberPrefix</c> (for example <c>analytics-material-published</c>).
/// </para>
/// </remarks>
public static class SubscriberMessagingServiceCollectionExtensions
{
    /// <summary>Registers MassTransit over RabbitMQ with the given consumers, retry and queue conventions, but without outbox and inbox.</summary>
    /// <remarks>
    /// The process cannot publish integration events (no outbox); a subscriber that needs to publish is a domain service and uses
    /// <see cref="MessagingServiceCollectionExtensions.AddAppMessaging{TWriteDbContext}"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddAppEventSubscriber(builder.Configuration, "analytics", bus =&gt; bus.AddConsumers(typeof(Program).Assembly));
    /// </code>
    /// </example>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">Application configuration with the <c>RabbitMq</c> connection string.</param>
    /// <param name="subscriberPrefix">Prefix of queue names (for example <c>analytics</c>), keeping this subscriber's queues apart from services' queues.</param>
    /// <param name="configureConsumers">Registers the consumers.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The <c>RabbitMq</c> connection string is missing or empty; thrown when the bus is configured at startup.</exception>
    public static IServiceCollection AddAppEventSubscriber(
        this IServiceCollection services,
        IConfiguration configuration,
        string subscriberPrefix,
        Action<IBusRegistrationConfigurator> configureConsumers)
    {
        if (BuildTimeDocumentGeneration.IsActive)
        {
            return services;
        }

        services.AddMassTransit(bus =>
        {
            bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(subscriberPrefix, includeNamespace: false));
            configureConsumers(bus);

            bus.AddConfigureEndpointsCallback((_, _, endpoint) =>
            {
                endpoint.UseMessageRetry(retry => retry.Intervals(100, 500, 1000, 5000));

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
}
