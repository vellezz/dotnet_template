using System.Collections.Concurrent;
using SuperApp.AnalyticsForwarder.Events;

namespace SuperApp.AnalyticsForwarder.Tests.Fakes;

/// <summary>Records captured product events so tests can assert the mapping of integration events.</summary>
internal sealed class RecordingProductEventSink : IProductEventSink
{
    public ConcurrentQueue<ProductEvent> Captured { get; } = new();

    public void Capture(ProductEvent productEvent) => Captured.Enqueue(productEvent);
}
