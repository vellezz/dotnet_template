using SuperApp.Framework.Application.Events;

namespace ServiceName.Application.Tests.Fakes;

/// <summary>
/// <see cref="IIntegrationEventPublisher"/> that records published integration events instead of writing them to the outbox,
/// so a test can assert which events a domain event handler published.
/// </summary>
internal sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
{
    /// <summary>Gets the published events in publication order.</summary>
    public List<object> Published { get; } = [];

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class
    {
        Published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
