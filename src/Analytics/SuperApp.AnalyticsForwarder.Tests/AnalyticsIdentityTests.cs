using SuperApp.Framework.Infrastructure.Analytics;
using Microsoft.Extensions.Options;

namespace SuperApp.AnalyticsForwarder.Tests;

/// <summary>The pseudonymous analytics identifier: stable per subject and key, unlinkable without the key, absent when analytics is off.</summary>
public sealed class AnalyticsIdentityTests
{
    private const string Key = "0123456789abcdef0123456789abcdef-test";

    [Fact]
    public void Same_subject_gets_the_same_identifier_in_the_expected_format()
    {
        var identity = Identity(Key);

        var first = identity.ForSubject("3c2f6a10-user");
        var second = identity.ForSubject("3c2f6a10-user");

        Assert.Equal(first, second);
        Assert.Matches("^u_[0-9a-f]{32}$", first);
        Assert.DoesNotContain("3c2f6a10", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Different_subjects_and_different_keys_give_different_identifiers()
    {
        Assert.NotEqual(Identity(Key).ForSubject("user-a"), Identity(Key).ForSubject("user-b"));
        Assert.NotEqual(Identity(Key).ForSubject("user-a"), Identity(Key + "-other").ForSubject("user-a"));
    }

    [Fact]
    public void No_identifier_without_subject_or_when_analytics_is_disabled()
    {
        Assert.Null(Identity(Key).ForSubject(null));
        Assert.Null(Identity(Key).ForSubject(string.Empty));
        Assert.Null(new AnalyticsIdentity(Options.Create(new AnalyticsOptions { IdKey = Key })).ForSubject("user-a"));
    }

    private static AnalyticsIdentity Identity(string key) =>
        new(Options.Create(new AnalyticsOptions { ProjectToken = "phc_test", IdKey = key }));
}
