using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace SuperApp.Framework.Infrastructure.Caching;

/// <summary>Registers the two-level cache (memory + Redis) and <see cref="FailSafeCache"/> for a service (ADR-0020).</summary>
/// <remarks>Called from the service's Infrastructure composition root (<c>Add{Service}Core</c>); see <see cref="AddAppCaching"/>.</remarks>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> with an in-memory L1 and, when a <c>Redis</c> connection string
    /// is configured, a Redis L2 shared by all replicas, plus the scoped <see cref="FailSafeCache"/> used by query handlers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without the <c>Redis</c> connection string (local development, tests) only the in-memory level is used; code using the cache does not change.
    /// Default entry lifetimes are 5 minutes overall and 30 seconds in memory; <see cref="FailSafeOptions"/> overrides them per entry.
    /// </para>
    /// <para>
    /// Services do not call this directly; <c>Add{Service}Core</c> in the Infrastructure project does. Cache only on the read side
    /// (query handlers, anti-corruption layer clients), never data used to make write decisions.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">Application configuration; the optional connection string <c>ConnectionStrings:Redis</c> enables the L2 level.</param>
    /// <param name="instancePrefix">
    /// Prefix of all Redis keys of this service (use the service schema name, e.g. <c>knowledge</c>), so that services sharing one Redis instance
    /// never read each other's entries.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddAppCaching(this IServiceCollection services, IConfiguration configuration, string instancePrefix)
    {
        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            var redisOptions = ConfigurationOptions.Parse(redis);
            redisOptions.AbortOnConnectFail = false;
            services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
            services.AddStackExchangeRedisCache(options =>
            {
                options.ConfigurationOptions = redisOptions;
                options.InstanceName = $"{instancePrefix}:";
            });
        }

        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromSeconds(30),
            };
        });

        services.AddScoped<FailSafeCache>();
        return services;
    }
}
