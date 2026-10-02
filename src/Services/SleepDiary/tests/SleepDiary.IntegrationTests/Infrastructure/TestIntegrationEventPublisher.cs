using SuperApp.Framework.Application.Events;

namespace SleepDiary.IntegrationTests.Infrastructure;

public sealed class TestIntegrationEventPublisher : IIntegrationEventPublisher
{
    public List<object> Published { get; } = [];

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
