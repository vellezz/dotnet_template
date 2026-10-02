using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Infrastructure.Persistence;
using SuperApp.Framework.Infrastructure.Telemetry;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace SuperApp.Framework.Infrastructure.Caching;

/// <summary>
/// Read-through cache for query results that keeps serving the last known value when refreshing it fails with a transient error
/// (database timeout or failover, lost connection, deadlock, network error). Built on <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> (ADR-0020).
/// </summary>
/// <remarks>
/// <para>Each entry has two ages, set with <see cref="FailSafeOptions"/>:</para>
/// <list type="bullet">
///   <item><description><b>Fresh</b> (younger than <see cref="FailSafeOptions.Fresh"/>): the value is returned directly.</description></item>
///   <item><description><b>Stale</b> (older than <c>Fresh</c> but younger than <c>Fresh + MaxStale</c>): one caller per process refreshes it
///   while concurrent callers immediately get the stale value. If the refresh fails with a transient error, the stale value is returned,
///   a warning is logged and the <c>superapp.cache.fail_safe.activations</c> counter is incremented.</description></item>
///   <item><description><b>Missing or expired</b>: the factory runs; <c>HybridCache</c> makes concurrent callers for the same key wait
///   for a single load (stampede protection). Errors of this first load are not hidden.</description></item>
/// </list>
/// <para>
/// Invalidate entries by tag with <see cref="RemoveByTagAsync"/> after the change is committed: a domain event handler registers the
/// invalidation with <c>IUnitOfWork.OnCommitted</c>. Invalidating directly in the handler, before the commit, would let a concurrent read
/// put the old data back into the cache. Use the cache only in query handlers and anti-corruption layer clients; never cache data that
/// commands rely on.
/// </para>
/// <para>
/// A permanent error during a refresh (invalid column after a missed migration, missing permission, a bug in the factory) is not hidden:
/// it propagates even when a stale value exists, so the problem is noticed instead of the cache silently serving old data until
/// <c>MaxStale</c> runs out.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // cache settings of the service (Knowledge.Infrastructure/Caching/KnowledgeCache.cs)
/// public static readonly FailSafeOptions Categories = new(Fresh: TimeSpan.FromMinutes(5), MaxStale: TimeSpan.FromHours(1));
///
/// // query handler
/// var categories = await cache.GetOrCreateAsync(
///     KnowledgeCache.CategoriesKey,
///     async token =&gt; (IReadOnlyList&lt;CategoryDto&gt;)await db.Categories.Select(...).ToListAsync(token),
///     KnowledgeCache.Categories,
///     [KnowledgeCache.CategoriesTag],
///     cancellationToken);
///
/// // domain event handler reacting to CategoryChanged (Knowledge.Infrastructure/Caching/CategoryCacheInvalidation.cs)
/// unitOfWork.OnCommitted(token =&gt; cache.RemoveByTagAsync(KnowledgeCache.CategoriesTag, token).AsTask());
/// </code>
/// </example>
/// <param name="cache">The two-level <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> registered by <c>AddAppCaching</c>.</param>
/// <param name="clock">Clock used to stamp and check freshness, so tests can move time.</param>
/// <param name="logger">Logger for fail-safe activations (event ID 300).</param>
public sealed partial class FailSafeCache(HybridCache cache, IClock clock, ILogger<FailSafeCache> logger)
{
    private static readonly TimeSpan DefaultLocalExpiration = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RefreshLocks = new();

    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, loading it with <paramref name="factory"/> when it is missing and refreshing it
    /// when it is stale, with the fail-safe behavior described on <see cref="FailSafeCache"/>.
    /// </summary>
    /// <typeparam name="T">Type of the cached value; must be JSON-serializable, because it is stored in Redis. Use DTOs, not EF entities.</typeparam>
    /// <param name="key">
    /// Cache key, unique within the service. Include every parameter the result depends on and a version segment that you bump whenever
    /// the shape of <typeparamref name="T"/> changes, e.g. <c>knowledge:material:v1:{id}</c>; otherwise replicas may read old JSON after a deployment.
    /// </param>
    /// <param name="factory">Loads the value from the source (usually a read-model query); receives the cancellation token.</param>
    /// <param name="options">Freshness, maximum staleness and in-memory lifetime of the entry.</param>
    /// <param name="tags">Tags used for invalidation with <see cref="RemoveByTagAsync"/>, or <see langword="null"/> when the entry is never invalidated explicitly.</param>
    /// <param name="cancellationToken">Cancellation of the request.</param>
    /// <returns>The fresh cached value, a newly loaded value, or, when refreshing failed with a transient error, the stale value.</returns>
    /// <exception cref="Exception">
    /// Any exception thrown by <paramref name="factory"/> when there is no cached value to fall back on, or when the error is not transient
    /// (anything other than a timeout, an HTTP error, a transient database error or a cancellation that was not requested by the caller).
    /// </exception>
    public async ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        FailSafeOptions options,
        IReadOnlyCollection<string>? tags,
        CancellationToken cancellationToken)
    {
        var entryOptions = new HybridCacheEntryOptions
        {
            Expiration = options.Fresh + options.MaxStale,
            LocalCacheExpiration = options.LocalExpiration ?? DefaultLocalExpiration,
        };

        var loaded = new StrongBox<bool>();
        var envelope = await cache.GetOrCreateAsync(
            key,
            (factory, clock, options.Fresh, loaded),
            static async (state, token) =>
            {
                state.loaded.Value = true;
                return new CacheEnvelope<T>(await state.factory(token), state.clock.UtcNow + state.Fresh);
            },
            entryOptions,
            tags,
            cancellationToken);

        if (envelope.FreshUntil > clock.UtcNow)
        {
            Record(loaded.Value ? "miss" : "fresh");
            return envelope.Value;
        }

        Record("stale");

        var refreshLock = RefreshLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        if (!await refreshLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return envelope.Value;
        }

        try
        {
            var value = await factory(cancellationToken);
            await cache.SetAsync(key, new CacheEnvelope<T>(value, clock.UtcNow + options.Fresh), entryOptions, tags, cancellationToken);
            return value;
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            InfrastructureTelemetry.CacheFailSafeActivations.Add(1);
            LogFailSafeActivated(logger, key, exception);
            return envelope.Value;
        }
        finally
        {
            // A caller that took this semaphore from the dictionary just before the removal may still acquire it and refresh once more
            // in parallel with a caller using a new semaphore. That costs one extra query, never a wrong value, and keeps the
            // dictionary from growing with every key ever refreshed.
            refreshLock.Release();
            RefreshLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(key, refreshLock));
        }
    }

    private static void Record(string result) =>
        InfrastructureTelemetry.CacheRequests.Add(1, new KeyValuePair<string, object?>("result", result));

    /// <summary>Invalidates every entry stored with <paramref name="tag"/>, in memory and in Redis.</summary>
    /// <remarks>
    /// Register the call with <c>IUnitOfWork.OnCommitted</c> from a domain event handler (for example <c>CategoryCacheInvalidation</c>
    /// handling <c>CategoryChanged</c>), so that it runs only after the change is committed; a rolled-back command then invalidates nothing,
    /// and a concurrent read cannot cache the old data again between the invalidation and the commit (ADR-0020). Other replicas may still
    /// serve their in-memory copy until <see cref="FailSafeOptions.LocalExpiration"/> passes; keep that value short.
    /// </remarks>
    /// <param name="tag">Tag given to the entries when they were stored.</param>
    /// <param name="cancellationToken">Cancellation of the surrounding operation.</param>
    /// <returns>A task that completes when the entries are invalidated.</returns>
    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken) =>
        cache.RemoveByTagAsync(tag, cancellationToken);

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        TimeoutException or HttpRequestException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => TransientSqlError.IsTransient(exception),
    };

    [LoggerMessage(300, LogLevel.Warning, "Cache fail-safe: returning stale value for {CacheKey}")]
    private static partial void LogFailSafeActivated(ILogger logger, string cacheKey, Exception exception);
}
