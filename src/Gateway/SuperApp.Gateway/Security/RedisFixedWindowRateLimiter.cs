using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace SuperApp.Gateway.Security;

/// <summary>
/// A distributed fixed-window rate limiter backed by Redis with automatic fallback to an in-memory limiter.
/// </summary>
/// <remarks>
/// All gateway replicas sharing the same Redis instance synchronize permit counts for each partition key.
/// If Redis is not configured, disconnected or encounters an error, the limiter falls back gracefully to a local in-memory
/// <see cref="FixedWindowRateLimiter"/> so traffic is not disrupted.
/// </remarks>
internal sealed class RedisFixedWindowRateLimiter : RateLimiter
{
    private static readonly TimeSpan DefaultExpirySlack = TimeSpan.FromSeconds(5);

    private readonly IConnectionMultiplexer? _redis;
    private readonly string _keyPrefix;
    private readonly string _partitionKey;
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private readonly TimeSpan _expirySlack;
    private readonly FixedWindowRateLimiter _fallbackLimiter;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisFixedWindowRateLimiter"/> class.
    /// </summary>
    /// <param name="redis">Redis connection multiplexer, or <see langword="null"/> to run entirely in-memory.</param>
    /// <param name="keyPrefix">Prefix for Redis keys (for example <c>gateway:rl:per-user</c>).</param>
    /// <param name="partitionKey">The partition key (for example a user ID or client IP).</param>
    /// <param name="permitLimit">The maximum number of permits allowed per window.</param>
    /// <param name="window">The duration of the rate limit window.</param>
    /// <param name="expirySlack">Optional buffer added to key expiration to account for clock drift.</param>
    public RedisFixedWindowRateLimiter(
        IConnectionMultiplexer? redis,
        string keyPrefix,
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        TimeSpan? expirySlack = null)
    {
        _redis = redis;
        _keyPrefix = keyPrefix;
        _partitionKey = partitionKey;
        _permitLimit = permitLimit;
        _window = window;
        _expirySlack = expirySlack ?? DefaultExpirySlack;
        _fallbackLimiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
        });
    }

    /// <inheritdoc />
    public override TimeSpan? IdleDuration => _fallbackLimiter.IdleDuration;

    /// <inheritdoc />
    public override RateLimiterStatistics? GetStatistics() => _fallbackLimiter.GetStatistics();

    /// <inheritdoc />
    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        if (_redis is null || !_redis.IsConnected)
        {
            return _fallbackLimiter.AttemptAcquire(permitCount);
        }

        try
        {
            var db = _redis.GetDatabase();
            var (redisKey, expireAtUtc, secondsRemaining) = ComputeWindowParameters();

            var transaction = db.CreateTransaction();
            var countTask = transaction.StringIncrementAsync((RedisKey)redisKey, permitCount);
            _ = transaction.KeyExpireAsync((RedisKey)redisKey, expireAtUtc);

            var committed = transaction.Execute();
            if (!committed)
            {
                return _fallbackLimiter.AttemptAcquire(permitCount);
            }

            return EvaluateCount(countTask.Result, secondsRemaining);
        }
        catch (Exception)
        {
            return _fallbackLimiter.AttemptAcquire(permitCount);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        if (_redis is null || !_redis.IsConnected)
        {
            return await _fallbackLimiter.AcquireAsync(permitCount, cancellationToken);
        }

        try
        {
            var db = _redis.GetDatabase();
            var (redisKey, expireAtUtc, secondsRemaining) = ComputeWindowParameters();

            var transaction = db.CreateTransaction();
            var countTask = transaction.StringIncrementAsync((RedisKey)redisKey, permitCount);
            _ = transaction.KeyExpireAsync((RedisKey)redisKey, expireAtUtc);

            var committed = await transaction.ExecuteAsync();
            if (!committed)
            {
                return await _fallbackLimiter.AcquireAsync(permitCount, cancellationToken);
            }

            var count = await countTask;
            return EvaluateCount(count, secondsRemaining);
        }
        catch (Exception)
        {
            return await _fallbackLimiter.AcquireAsync(permitCount, cancellationToken);
        }
    }

    private (string Key, DateTime ExpireAtUtc, long SecondsRemaining) ComputeWindowParameters()
    {
        var windowSeconds = Math.Max(1, (long)_window.TotalSeconds);
        var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var bucket = nowUnixSeconds / windowSeconds;
        var key = $"{_keyPrefix}:{_partitionKey}:{bucket}";
        var expireAtUnixSeconds = ((bucket + 1) * windowSeconds) + Math.Max(1, (long)_expirySlack.TotalSeconds);
        var expireAtUtc = DateTimeOffset.FromUnixTimeSeconds(expireAtUnixSeconds).UtcDateTime;
        var secondsRemaining = windowSeconds - (nowUnixSeconds % windowSeconds);
        return (key, expireAtUtc, secondsRemaining);
    }

    private RateLimitLease EvaluateCount(long count, long secondsRemaining)
    {
        if (count <= _permitLimit)
        {
            return AppRateLimitLease.Successful;
        }

        return AppRateLimitLease.Failed(TimeSpan.FromSeconds(Math.Max(1, secondsRemaining)));
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fallbackLimiter.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    protected override ValueTask DisposeAsyncCore() => _fallbackLimiter.DisposeAsync();
}
