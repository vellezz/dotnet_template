using SuperApp.Framework.Application.Events;

namespace Knowledge.Application.Tests.Fakes;

/// <summary>In-memory <see cref="IIntegrationEventPublisher"/> that records every published integration event.</summary>
internal sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
{
    public List<object> Published { get; } = [];

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class
    {
        Published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
