using SuperApp.Framework.Infrastructure.Analytics;

namespace SuperApp.AnalyticsForwarder.Events;

/// <summary>Registers the <see cref="IProductEventSink"/> matching the analytics settings (ADR-0036).</summary>
/// <remarks>
/// With analytics enabled (<c>Analytics:ProjectToken</c> set) events go to PostHog through <see cref="PostHogProductEventSink"/> and are flushed
/// on shutdown; otherwise <see cref="LoggingProductEventSink"/> only logs their names. Call after <c>AddAppAnalytics</c>, which registers the
/// PostHog client and <see cref="AnalyticsIdentity"/>.
/// </remarks>
internal static class ProductEventSinkServiceCollectionExtensions
{
    /// <summary>Registers the sink as a singleton, plus the shutdown flush when PostHog is used.</summary>
    /// <param name="services">The service collection of the forwarder.</param>
    /// <param name="configuration">Application configuration with the optional <c>Analytics</c> section.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddProductEventSink(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(AnalyticsOptions.SectionName).Get<AnalyticsOptions>() ?? new AnalyticsOptions();
        if (!settings.Enabled)
        {
            return services.AddSingleton<IProductEventSink, LoggingProductEventSink>();
        }

        services.AddSingleton<IProductEventSink, PostHogProductEventSink>();
        services.AddHostedService<PostHogFlushOnShutdown>();
        return services;
    }
}
