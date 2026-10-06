using SuperApp.AnalyticsForwarder.Events;
using SuperApp.Framework.Infrastructure.Analytics;

namespace SuperApp.AnalyticsForwarder.Tests;

public sealed class FeatureFlagsCacheTests
{
    [Fact]
    public void System_flags_can_be_updated_and_retrieved()
    {
        var cache = new FeatureFlagsCache();
        var initial = cache.GetFlags(AnalyticsIdentity.System);
        Assert.Empty(initial);

        var updated = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["flag_alpha"] = true,
            ["flag_beta"] = false,
        };

        cache.UpdateSystemFlags(updated);

        var retrieved = cache.GetFlags(AnalyticsIdentity.System);
        Assert.True(retrieved["flag_alpha"]);
        Assert.False(retrieved["flag_beta"]);
        Assert.True(retrieved["FLAG_ALPHA"]); // case-insensitive
    }

    [Fact]
    public void User_flags_take_precedence_until_expired()
    {
        var cache = new FeatureFlagsCache();
        cache.UpdateSystemFlags(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["flag_alpha"] = true,
        });

        cache.SetUserFlags("user-distinct-1", new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["flag_alpha"] = false,
        }, TimeSpan.FromMinutes(5));

        var userFlags = cache.GetFlags("user-distinct-1");
        Assert.False(userFlags["flag_alpha"]);

        var otherUserFlags = cache.GetFlags("user-distinct-2");
        Assert.True(otherUserFlags["flag_alpha"]);
    }

    [Fact]
    public void Expired_user_flags_fallback_to_system_flags()
    {
        var cache = new FeatureFlagsCache();
        cache.UpdateSystemFlags(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["flag_alpha"] = true,
        });

        // Set expired flags
        cache.SetUserFlags("user-distinct-expired", new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["flag_alpha"] = false,
        }, TimeSpan.FromMilliseconds(-100));

        var flags = cache.GetFlags("user-distinct-expired");
        Assert.True(flags["flag_alpha"]);
    }
}
