using SuperApp.Framework.Application.Events;
using SuperApp.Framework.Domain.Events;
using SuperApp.Framework.Infrastructure.Telemetry;
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SuperApp.Framework.Infrastructure.Events;

/// <summary>
/// Runtime <see cref="IDomainEventDispatcher"/>: resolves the handlers of each event from the current DI scope, the same scope as the write
/// context, so integration events they publish land in the outbox of the same save (ADR-0027). Each dispatch gets its own tracing span.
/// </summary>
internal sealed partial class DomainEventDispatcher(IServiceProvider serviceProvider, ILogger<DomainEventDispatcher> logger)
    : IDomainEventDispatcher
{
    private static readonly MethodInfo InvokeHandlersMethod =
        typeof(DomainEventDispatcher).GetMethod(nameof(InvokeHandlersAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> Invokers = new();

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var eventName = domainEvent.GetType().Name;
        using var activity = InfrastructureTelemetry.ActivitySource.StartActivity($"domain-event {eventName}");
        LogDispatching(logger, eventName);

        var invoker = Invokers.GetOrAdd(domainEvent.GetType(), static type =>
            InvokeHandlersMethod.MakeGenericMethod(type).CreateDelegate<Func<IServiceProvider, IDomainEvent, CancellationToken, Task>>());

        await invoker(serviceProvider, domainEvent, cancellationToken);
    }

    private static async Task InvokeHandlersAsync<TEvent>(IServiceProvider provider, IDomainEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in provider.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
        }
    }

    [LoggerMessage(200, LogLevel.Debug, "Dispatching domain event {EventName}")]
    private static partial void LogDispatching(ILogger logger, string eventName);
}
