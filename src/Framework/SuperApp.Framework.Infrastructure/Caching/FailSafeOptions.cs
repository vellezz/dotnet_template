namespace SuperApp.Framework.Infrastructure.Caching;

/// <summary>Lifetime settings of one kind of <see cref="FailSafeCache"/> entry (ADR-0020).</summary>
/// <remarks>
/// Define one instance per kind of cached data as a static field of the service's cache settings class, so values are tuned in one place.
/// Choose <paramref name="Fresh"/> by how long users may see outdated data, and <paramref name="MaxStale"/> by how long the service
/// should keep answering during an outage of its database or of a called service.
/// </remarks>
/// <param name="Fresh">How long a value is returned without being refreshed.</param>
/// <param name="MaxStale">How long after <paramref name="Fresh"/> a stale value may still be returned when refreshing fails; after that the entry is gone.</param>
/// <param name="LocalExpiration">
/// Lifetime in the in-memory level of each replica; <see langword="null"/> means 30 seconds. Keep it short: tag invalidation does not clear
/// the memory of other replicas immediately.
/// </param>
public sealed record FailSafeOptions(TimeSpan Fresh, TimeSpan MaxStale, TimeSpan? LocalExpiration = null);
