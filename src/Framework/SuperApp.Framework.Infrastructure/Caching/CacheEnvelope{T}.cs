namespace SuperApp.Framework.Infrastructure.Caching;

/// <summary>
/// Wrapper stored in the cache by <see cref="FailSafeCache"/>: the cached value plus the moment until which it counts as fresh.
/// </summary>
/// <remarks>
/// Not meant to be used directly. It is public only because the L2 cache (Redis) serializes it with <c>System.Text.Json</c>.
/// Keeping the freshness timestamp inside the entry, instead of relying on cache expiration, is what allows a stale value to be served
/// while it is being refreshed, or when the refresh fails (ADR-0020).
/// </remarks>
/// <typeparam name="T">Type of the cached value; must be JSON-serializable because it is stored in Redis.</typeparam>
/// <param name="Value">The cached value, as returned by the factory.</param>
/// <param name="FreshUntil">UTC instant after which the value is stale: it is refreshed on the next read, but may still be returned by the fail-safe.</param>
public sealed record CacheEnvelope<T>(T Value, DateTimeOffset FreshUntil);
