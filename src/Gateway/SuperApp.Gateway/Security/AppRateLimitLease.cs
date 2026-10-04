using System.Threading.RateLimiting;

namespace SuperApp.Gateway.Security;

/// <summary>
/// A rate limit lease returned by <see cref="RedisFixedWindowRateLimiter"/>.
/// </summary>
internal sealed class AppRateLimitLease : RateLimitLease
{
    /// <summary>A singleton representing an acquired lease.</summary>
    public static readonly AppRateLimitLease Successful = new(true, null);

    private readonly TimeSpan? _retryAfter;

    private AppRateLimitLease(bool isAcquired, TimeSpan? retryAfter)
    {
        IsAcquired = isAcquired;
        _retryAfter = retryAfter;
    }

    /// <summary>Creates a lease representing a rate-limit rejection with an optional retry-after delay.</summary>
    /// <param name="retryAfter">The duration until the current window resets.</param>
    /// <returns>A rejected rate limit lease.</returns>
    public static AppRateLimitLease Failed(TimeSpan retryAfter) => new(false, retryAfter);

    /// <inheritdoc />
    public override bool IsAcquired { get; }

    /// <inheritdoc />
    public override IEnumerable<string> MetadataNames =>
        _retryAfter.HasValue ? [MetadataName.RetryAfter.Name] : [];

    /// <inheritdoc />
    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        if (metadataName == MetadataName.RetryAfter.Name && _retryAfter.HasValue)
        {
            metadata = _retryAfter.Value;
            return true;
        }

        metadata = null;
        return false;
    }
}
