using System.Collections.Concurrent;
using SuperApp.Framework.Infrastructure.Analytics;

namespace SuperApp.AnalyticsForwarder.Events;

/// <summary>
/// Thread-safe in-memory cache of feature flag evaluations in the forwarder process.
/// </summary>
internal sealed class FeatureFlagsCache
{
    private IReadOnlyDictionary<string, bool> _systemFlags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (IReadOnlyDictionary<string, bool> Flags, DateTimeOffset Expiry)> _userEvaluations = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the cached flags for the specified distinct identifier.
    /// </summary>
    /// <param name="distinctId">The user pseudonym or <see cref="AnalyticsIdentity.System"/>.</param>
    /// <returns>Cached dictionary of flags, or system flags if user-specific evaluations are absent.</returns>
    public IReadOnlyDictionary<string, bool> GetFlags(string distinctId)
    {
        if (distinctId == AnalyticsIdentity.System)
        {
            return _systemFlags;
        }

        if (_userEvaluations.TryGetValue(distinctId, out var entry) && entry.Expiry > DateTimeOffset.UtcNow)
        {
            return entry.Flags;
        }

        return _systemFlags;
    }

    /// <summary>
    /// Updates the baseline system flags refreshed periodically from PostHog.
    /// </summary>
    /// <param name="flags">The updated dictionary of flags.</param>
    public void UpdateSystemFlags(IReadOnlyDictionary<string, bool> flags)
    {
        _systemFlags = flags;
    }

    /// <summary>
    /// Stores or updates evaluated flags for a specific distinct identifier with a retention duration.
    /// </summary>
    /// <param name="distinctId">The user pseudonym.</param>
    /// <param name="flags">The evaluated dictionary of flags.</param>
    /// <param name="ttl">Duration before the evaluation expires.</param>
    public void SetUserFlags(string distinctId, IReadOnlyDictionary<string, bool> flags, TimeSpan ttl)
    {
        _userEvaluations[distinctId] = (flags, DateTimeOffset.UtcNow.Add(ttl));
    }
}
