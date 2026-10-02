using SuperApp.Framework.Application.Events;

namespace SleepDiary.Application.Tests.Fakes;

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
