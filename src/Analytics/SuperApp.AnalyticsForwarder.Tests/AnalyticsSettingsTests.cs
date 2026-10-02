using SuperApp.Framework.Application.FeatureFlags;
using SuperApp.Framework.Infrastructure.Analytics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SuperApp.AnalyticsForwarder.Tests;

/// <summary>
/// <c>AddAppAnalytics</c>: settings are validated when analytics is enabled, and with analytics disabled flags come from configuration
/// with the code default as fallback.
/// </summary>
public sealed class AnalyticsSettingsTests
{
    private static readonly FeatureFlag Ratings = new("knowledge_material_ratings", DefaultValue: false);
    private static readonly FeatureFlag KillSwitch = new("knowledge_search", DefaultValue: true);

    [Theory]
    [InlineData(null, "https://eu.i.posthog.com")]
    [InlineData("too-short", "https://eu.i.posthog.com")]
    [InlineData("0123456789abcdef0123456789abcdef", "https://evil.example.com")]
    [InlineData("0123456789abcdef0123456789abcdef", "http://eu.i.posthog.com")]
    public void Enabled_analytics_requires_a_long_id_key_and_a_posthog_https_host(string? idKey, string host)
    {
        using var provider = Provider(new()
        {
            ["Analytics:ProjectToken"] = "phc_test",
            ["Analytics:IdKey"] = idKey,
            ["Analytics:Host"] = host,
        });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AnalyticsOptions>>().Value);
    }

    [Fact]
    public async Task Disabled_analytics_reads_flags_from_configuration_and_falls_back_to_the_default()
    {
        using var provider = Provider(new() { ["FeatureFlags:knowledge_material_ratings"] = "true" });
        await using var scope = provider.CreateAsyncScope();
        var flags = scope.ServiceProvider.GetRequiredService<IFeatureFlags>();

        Assert.True(await flags.IsEnabledAsync(Ratings, TestContext.Current.CancellationToken));
        Assert.True(await flags.IsEnabledAsync(KillSwitch, TestContext.Current.CancellationToken));
        Assert.False(provider.GetRequiredService<IOptions<AnalyticsOptions>>().Value.Enabled);
    }

    [Fact]
    public void Enabled_analytics_in_a_process_without_users_builds_with_validation()
    {
        // The gateway calls only AddAppAnalytics and has no ICurrentUser; in Development the host validates the whole container on build,
        // so nothing registered by AddAppAnalytics may depend on ICurrentUser.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Enabled).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAppAnalytics(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Matches("^u_[0-9a-f]{32}$", provider.GetRequiredService<AnalyticsIdentity>().ForSubject("user-a"));
        Assert.Null(provider.GetService<IFeatureFlags>());
    }

    private static readonly Dictionary<string, string?> Enabled = new()
    {
        ["Analytics:ProjectToken"] = "phc_test_not_a_real_project",
        ["Analytics:IdKey"] = "0123456789abcdef0123456789abcdef",
    };

    private static ServiceProvider Provider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAppFeatureFlags(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
