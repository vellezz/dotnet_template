using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SuperApp.Framework.Infrastructure.Analytics;
using StackExchange.Redis;

namespace SuperApp.AnalyticsForwarder.Events;

/// <summary>Registers the <see cref="IProductEventSink"/> matching the analytics settings (ADR-0036).</summary>
/// <remarks>
/// With analytics enabled (<c>Analytics:ProjectToken</c> set) events go to PostHog through <see cref="PostHogProductEventSink"/> and are flushed
/// on shutdown; otherwise <see cref="LoggingProductEventSink"/> only logs their names.
/// Both variants are decorated with <see cref="DeduplicatingProductEventSink"/> to ensure at-least-once message delivery from RabbitMQ
/// does not forward duplicate events (D9).
/// Call after <c>AddAppAnalytics</c>, which registers the PostHog client and <see cref="AnalyticsIdentity"/>.
/// </remarks>
internal static class ProductEventSinkServiceCollectionExtensions
{
    /// <summary>Registers the sink as a singleton, plus the shutdown flush when PostHog is used.</summary>
    /// <param name="services">The service collection of the forwarder.</param>
    /// <param name="configuration">Application configuration with the optional <c>Analytics</c> section.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddProductEventSink(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();

        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            var redisOptions = ConfigurationOptions.Parse(redis);
            redisOptions.AbortOnConnectFail = false;
            services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
        }

        var settings = configuration.GetSection(AnalyticsOptions.SectionName).Get<AnalyticsOptions>() ?? new AnalyticsOptions();
        if (!settings.Enabled)
        {
            services.AddSingleton<LoggingProductEventSink>();
            services.AddSingleton<IProductEventSink>(sp =>
                new DeduplicatingProductEventSink(
                    sp.GetRequiredService<LoggingProductEventSink>(),
                    sp.GetRequiredService<IMemoryCache>(),
                    sp.GetRequiredService<ILogger<DeduplicatingProductEventSink>>(),
                    sp.GetService<IConnectionMultiplexer>()));
            return services;
        }

        services.AddSingleton<PostHogProductEventSink>();
        services.AddSingleton<IProductEventSink>(sp =>
            new DeduplicatingProductEventSink(
                sp.GetRequiredService<PostHogProductEventSink>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<ILogger<DeduplicatingProductEventSink>>(),
                sp.GetService<IConnectionMultiplexer>()));
        services.AddHostedService<PostHogFlushOnShutdown>();
        return services;
    }
}
