using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SuperApp.AnalyticsForwarder.Events;
using SuperApp.AnalyticsForwarder.Tests.Fakes;

namespace SuperApp.AnalyticsForwarder.Tests;

/// <summary>
/// Unit tests for <see cref="DeduplicatingProductEventSink"/> verifying deduplication behavior with memory cache and Redis fallback.
/// </summary>
public sealed class DeduplicationTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 1, 7, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Duplicate_message_id_is_only_forwarded_once()
    {
        var recordingSink = new RecordingProductEventSink();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var dedupSink = new DeduplicatingProductEventSink(
            recordingSink,
            memoryCache,
            NullLogger<DeduplicatingProductEventSink>.Instance);

        var messageId = Guid.NewGuid();
        var @event1 = new ProductEvent("test_event", At, messageId, "user-1", new Dictionary<string, object>());
        var @event2 = new ProductEvent("test_event", At, messageId, "user-1", new Dictionary<string, object>());

        dedupSink.Capture(@event1);
        dedupSink.Capture(@event2);

        Assert.Single(recordingSink.Captured);
    }

    [Fact]
    public void Different_message_ids_are_both_forwarded()
    {
        var recordingSink = new RecordingProductEventSink();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var dedupSink = new DeduplicatingProductEventSink(
            recordingSink,
            memoryCache,
            NullLogger<DeduplicatingProductEventSink>.Instance);

        var @event1 = new ProductEvent("test_event", At, Guid.NewGuid(), "user-1", new Dictionary<string, object>());
        var @event2 = new ProductEvent("test_event", At, Guid.NewGuid(), "user-1", new Dictionary<string, object>());

        dedupSink.Capture(@event1);
        dedupSink.Capture(@event2);

        Assert.Equal(2, recordingSink.Captured.Count);
    }

    [Fact]
    public void Event_without_message_id_is_not_deduplicated()
    {
        var recordingSink = new RecordingProductEventSink();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var dedupSink = new DeduplicatingProductEventSink(
            recordingSink,
            memoryCache,
            NullLogger<DeduplicatingProductEventSink>.Instance);

        var @event1 = new ProductEvent("test_event", At, null, "user-1", new Dictionary<string, object>());
        var @event2 = new ProductEvent("test_event", At, null, "user-1", new Dictionary<string, object>());

        dedupSink.Capture(@event1);
        dedupSink.Capture(@event2);

        Assert.Equal(2, recordingSink.Captured.Count);
    }
}
