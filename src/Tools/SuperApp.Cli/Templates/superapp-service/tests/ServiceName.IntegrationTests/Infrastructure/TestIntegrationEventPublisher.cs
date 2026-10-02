using SuperApp.Framework.Application.Events;

namespace ServiceName.IntegrationTests.Infrastructure;

/// <summary>
/// <see cref="IIntegrationEventPublisher"/> that records published integration events instead of sending them to RabbitMQ; the integration
/// tests run without MassTransit (ADR-0005).
/// </summary>
/// <remarks>
/// Shared through <see cref="ServiceFixture.Publisher"/> by all tests, which may run in parallel: adding is synchronized, and assertions
/// should look for the specific event of the test (e.g. by ID) rather than count all events.
/// </remarks>
public sealed class TestIntegrationEventPublisher : IIntegrationEventPublisher
{
    /// <summary>Gets all events published since the fixture started, in publication order.</summary>
    public List<object> Published { get; } = [];

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class
    {
        lock (Published)
        {
            Published.Add(integrationEvent);
        }

        return Task.CompletedTask;
    }
}
