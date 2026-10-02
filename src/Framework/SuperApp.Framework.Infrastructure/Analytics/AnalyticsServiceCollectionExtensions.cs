using SuperApp.Framework.Application.FeatureFlags;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Infrastructure.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PostHog;
using PostHog.Config;

namespace SuperApp.Framework.Infrastructure.Analytics;

/// <summary>Registers product analytics and feature flags (PostHog Cloud EU, ADR-0036).</summary>
/// <remarks>
/// Product analytics has three parts with different owners: web and mobile clients capture user interactions (through the BFF proxy
/// <c>/ingest</c> or the mobile SDKs), the analytics forwarder turns integration events into backend events, and services evaluate
/// feature flags through <see cref="IFeatureFlags"/>. This class covers the parts shared by processes: settings, the pseudonymous identifier,
/// the PostHog client (<see cref="AddAppAnalytics"/>) and flag evaluation (<see cref="AddAppFeatureFlags"/>).
/// </remarks>
public static class AnalyticsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AnalyticsOptions"/> (validated at startup), <see cref="AnalyticsIdentity"/> and, when analytics is enabled, the
    /// PostHog .NET client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by every process that identifies users or talks to PostHog: the gateway (<c>analyticsId</c> in <c>/bff/user</c>, the
    /// <c>/ingest</c> proxy), the analytics forwarder, and services through <see cref="AddAppFeatureFlags"/>. With analytics enabled
    /// (<c>Analytics:ProjectToken</c> set) a missing or short <c>Analytics:IdKey</c> or a host outside <c>posthog.com</c> stops the process at
    /// startup. The client is a singleton created on first use; it batches events and sends them in the background.
    /// </para>
    /// <para>
    /// Domain services never capture events themselves: backend events reach PostHog only through the analytics forwarder, which subscribes
    /// to integration events (ADR-0036); the architecture tests allow the SDK only in the framework and the forwarder.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection of the process.</param>
    /// <param name="configuration">Application configuration with the optional <c>Analytics</c> section.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddAppAnalytics(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AnalyticsOptions>()
            .Bind(configuration.GetSection(AnalyticsOptions.SectionName))
            .Validate(
                options => options.IsValid(),
                "Analytics: with ProjectToken set, IdKey (at least 32 characters) is required and Host/AssetsHost must be https://*.posthog.com.")
            .ValidateOnStart();
        services.TryAddSingleton<AnalyticsIdentity>();

        var settings = Settings(configuration);
        if (!settings.Enabled || BuildTimeDocumentGeneration.IsActive || services.Any(descriptor => descriptor.ServiceType == typeof(IPostHogClient)))
        {
            return services;
        }

        services.AddPostHog();
        services.PostConfigure<PostHogOptions>(posthog =>
        {
            posthog.ProjectToken = settings.ProjectToken;
            posthog.HostUrl = settings.Host;
            posthog.SecretKey = settings.FeatureFlagsKey;
        });
        return services;
    }

    /// <summary>
    /// Registers <see cref="IFeatureFlags"/> for handlers: <see cref="PostHogFeatureFlags"/> (scoped) when analytics is enabled,
    /// <see cref="ConfigurationFeatureFlags"/> otherwise; also calls <see cref="AddAppAnalytics"/>.
    /// </summary>
    /// <remarks>
    /// Called by each service's Infrastructure composition root (<c>Add{Service}Core</c>). Flags are evaluated for
    /// <see cref="ICurrentUser"/>, so the host must register it: <c>AddAppApi</c> in an API, <c>AddAppWorker</c> in a Worker, a fake in tests.
    /// Without analytics (locally and in tests) flags come from configuration (<c>FeatureFlags:{key}</c>) with the code default as
    /// fallback, and nothing is sent anywhere.
    /// </remarks>
    /// <param name="services">The service collection of the service process or test.</param>
    /// <param name="configuration">Application configuration with the optional <c>Analytics</c> and <c>FeatureFlags</c> sections.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddAppFeatureFlags(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAppAnalytics(configuration);

        if (Settings(configuration).Enabled && !BuildTimeDocumentGeneration.IsActive)
        {
            services.TryAddScoped<IFeatureFlags, PostHogFeatureFlags>();
        }
        else
        {
            services.TryAddSingleton<IFeatureFlags, ConfigurationFeatureFlags>();
        }

        return services;
    }

    private static AnalyticsOptions Settings(IConfiguration configuration) =>
        configuration.GetSection(AnalyticsOptions.SectionName).Get<AnalyticsOptions>() ?? new AnalyticsOptions();
}
