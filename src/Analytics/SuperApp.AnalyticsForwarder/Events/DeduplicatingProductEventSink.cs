using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;

namespace SuperApp.AnalyticsForwarder.Events;

/// <summary>
/// Decorates an <see cref="IProductEventSink"/> with distributed and in-memory deduplication based on <see cref="ProductEvent.MessageId"/> (ADR-0036, D9).
/// </summary>
/// <remarks>
/// RabbitMQ provides at-least-once delivery without an inbox in the forwarder. If a message is redelivered
/// after worker restart or network timeout, this sink prevents forwarding identical events to PostHog more than once.
/// If Redis is configured and available, deduplication is synchronized across all forwarder replicas via Redis keys with TTL;
/// otherwise, it falls back to an in-memory cache.
/// </remarks>
internal sealed partial class DeduplicatingProductEventSink : IProductEventSink
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    private readonly IProductEventSink _inner;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<DeduplicatingProductEventSink> _logger;
    private readonly IConnectionMultiplexer? _redis;
    private readonly TimeSpan _ttl;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeduplicatingProductEventSink"/> class.
    /// </summary>
    /// <param name="inner">The underlying sink that actually captures the events.</param>
    /// <param name="memoryCache">The memory cache for local deduplication fallback.</param>
    /// <param name="logger">The logger for deduplication diagnostic events.</param>
    /// <param name="redis">Optional Redis connection multiplexer for distributed deduplication.</param>
    /// <param name="ttl">Optional retention duration for message deduplication markers (defaults to 24 hours).</param>
    public DeduplicatingProductEventSink(
        IProductEventSink inner,
        IMemoryCache memoryCache,
        ILogger<DeduplicatingProductEventSink> logger,
        IConnectionMultiplexer? redis = null,
        TimeSpan? ttl = null)
    {
        _inner = inner;
        _memoryCache = memoryCache;
        _logger = logger;
        _redis = redis;
        _ttl = ttl ?? DefaultTtl;
    }

    /// <inheritdoc />
    public void Capture(ProductEvent productEvent)
    {
        if (productEvent.MessageId is not { } messageId)
        {
            _inner.Capture(productEvent);
            return;
        }

        var key = $"analytics:dedup:{messageId}";

        if (_redis is not null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var isNew = db.StringSet((RedisKey)key, "1", _ttl, When.NotExists);
                if (!isNew)
                {
                    LogDuplicateSkipped(_logger, productEvent.Name, messageId);
                    return;
                }

                _inner.Capture(productEvent);
                return;
            }
            catch (Exception ex)
            {
                LogRedisFallback(_logger, messageId, ex);
            }
        }

        if (IsDuplicateInMemory(key))
        {
            LogDuplicateSkipped(_logger, productEvent.Name, messageId);
            return;
        }

        _inner.Capture(productEvent);
    }

    private bool IsDuplicateInMemory(string key)
    {
        if (_memoryCache.TryGetValue(key, out _))
        {
            return true;
        }

        _memoryCache.Set(key, true, _ttl);
        return false;
    }

    [LoggerMessage(9003, LogLevel.Information, "Product event {EventName} with message ID {MessageId} was already processed (duplicate); skipping")]
    private static partial void LogDuplicateSkipped(ILogger logger, string eventName, Guid messageId);

    [LoggerMessage(9004, LogLevel.Warning, "Failed to check Redis deduplication for message {MessageId}; falling back to memory cache")]
    private static partial void LogRedisFallback(ILogger logger, Guid messageId, Exception ex);
}
