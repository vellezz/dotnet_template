using System.Net;
using System.Text;
using System.Text.Json;
using SuperApp.AnalyticsForwarder.Tests.Fakes;
using SuperApp.Framework.Application.FeatureFlags;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Infrastructure.Analytics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SuperApp.AnalyticsForwarder.Tests;

public sealed class HybridCacheFeatureFlagsTests
{
    private static readonly FeatureFlag EnabledByDefault = new("feature_default_on", DefaultValue: true);
    private static readonly FeatureFlag DisabledByDefault = new("feature_default_off", DefaultValue: false);

    [Fact]
    public async Task Evaluates_flag_from_forwarder_and_serves_subsequent_calls_from_cache()
    {
        var testHandler = new FakeForwarderHandler(_ =>
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, bool>
            {
                ["feature_default_off"] = true,
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });

        using var provider = CreateProvider(testHandler, new FakeCurrentUser("test-user-1"));
        await using var scope = provider.CreateAsyncScope();
        var featureFlags = scope.ServiceProvider.GetRequiredService<IFeatureFlags>();

        var result1 = await featureFlags.IsEnabledAsync(DisabledByDefault, TestContext.Current.CancellationToken);
        Assert.True(result1);
        Assert.Equal(1, testHandler.CallCount);

        // Second call uses HybridCache L1/L2 and does not call the forwarder again
        var result2 = await featureFlags.IsEnabledAsync(DisabledByDefault, TestContext.Current.CancellationToken);
        Assert.True(result2);
        Assert.Equal(1, testHandler.CallCount);
    }

    [Fact]
    public async Task Falls_back_to_default_when_flag_not_in_forwarder_response()
    {
        var testHandler = new FakeForwarderHandler(_ =>
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, bool>
            {
                ["unrelated_flag"] = true,
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });

        using var provider = CreateProvider(testHandler, new FakeCurrentUser("test-user-2"));
        await using var scope = provider.CreateAsyncScope();
        var featureFlags = scope.ServiceProvider.GetRequiredService<IFeatureFlags>();

        var resultOff = await featureFlags.IsEnabledAsync(DisabledByDefault, TestContext.Current.CancellationToken);
        Assert.False(resultOff);

        var resultOn = await featureFlags.IsEnabledAsync(EnabledByDefault, TestContext.Current.CancellationToken);
        Assert.True(resultOn);
    }

    [Fact]
    public async Task Falls_back_to_default_when_forwarder_fails()
    {
        var testHandler = new FakeForwarderHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        using var provider = CreateProvider(testHandler, new FakeCurrentUser("test-user-3"));
        await using var scope = provider.CreateAsyncScope();
        var featureFlags = scope.ServiceProvider.GetRequiredService<IFeatureFlags>();

        var result = await featureFlags.IsEnabledAsync(EnabledByDefault, TestContext.Current.CancellationToken);
        Assert.True(result);
    }

    private static ServiceProvider CreateProvider(HttpMessageHandler handler, ICurrentUser currentUser)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:ProjectToken"] = "phc_test_dummy",
            ["Analytics:IdKey"] = "0123456789abcdef0123456789abcdef",
            ["Analytics:ForwarderUrl"] = "http://localhost:5180",
            ["Analytics:FeatureFlagsTimeout"] = "00:00:02",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(currentUser);
        services.AddAppFeatureFlags(configuration);
        services.AddHttpClient("AnalyticsForwarder")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class FakeForwarderHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(handler(request));
        }
    }
}
