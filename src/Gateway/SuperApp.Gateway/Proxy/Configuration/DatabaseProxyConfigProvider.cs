using SuperApp.Gateway.Hosting;
using SuperApp.Gateway.Telemetry;
using System.Diagnostics.Metrics;
using SuperApp.Gateway.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace SuperApp.Gateway.Proxy.Configuration;

/// <summary>
/// YARP <see cref="IProxyConfigProvider"/> that loads the routes and clusters of the current gateway profile from the <c>gateway</c> schema
/// in MSSQL and reloads them, without a pod restart, when a new gateway migration has been applied (ADR-0022).
/// </summary>
/// <remarks>
/// <para><b>How route configuration is changed.</b> Nobody edits the tables by hand (the gateway's database user only has <c>SELECT</c>
/// on them). A developer changes <see cref="SuperApp.Gateway.Persistence.Seed.ProxyConfigurationSeed"/> (<c>HasData</c>), generates a new migration of
/// <see cref="SuperApp.Gateway.Persistence.GatewayDbContext"/> and gets it reviewed. <c>SuperApp.Migrator</c> (dev/test) or the DBA script (prod) applies it, which writes the
/// data changes and a new row in <c>gateway.__EFMigrationsHistory</c> in one transaction.</para>
/// <para><b>How it is reloaded.</b> The id of the last applied migration is the configuration version:</para>
/// <list type="number">
///   <item><description>Every <c>Gateway:ConfigPollInterval</c> (default 5 seconds) each replica reads the applied migrations. If the last
///   one equals the active <see cref="ActiveMigrationId"/>, nothing else happens.</description></item>
///   <item><description>If it equals the migration id that was already rejected (see below), nothing else happens either.</description></item>
///   <item><description>Otherwise it reads all rows of its profile into a <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigSnapshot"/>, maps them with
///   <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigMapper"/> (which rejects unsupported transform kinds and destination addresses outside the
///   cluster, see <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyDestinationAddress"/>) and validates every route and cluster with YARP's
///   <see cref="IConfigValidator"/> (this also checks that the referenced authorization and rate limiting policies exist in code).</description></item>
///   <item><description>Only a valid configuration replaces the active one; the old change token is signalled so YARP switches to the new
///   routes. The snapshot is then written to <see cref="HybridCache"/> (L2 Redis, key <c>gateway:yarp-config:v1:{profile}</c>, 30 days)
///   as the last known good configuration.</description></item>
/// </list>
/// <para><b>Failure behavior.</b> An invalid configuration (mapping errors or YARP validation errors) never replaces a working one: it is
/// logged (event 3002) and counted in <see cref="SuperApp.Gateway.Telemetry.GatewayTelemetry.ProxyConfigReloadFailures"/> once per migration id. The replica
/// remembers the rejected migration id and does not read, log or count it again; only a newer migration (the fix) triggers the next reload.
/// A database outage or another unexpected error while running keeps the current configuration and is logged and counted on every poll
/// (event 3003), because it is not tied to a configuration version.
/// If the database is unavailable at startup, the last known good snapshot is taken from the cache (event 3004); with neither database nor
/// cache entry nothing is loaded, <see cref="IsLoaded"/> stays <see langword="false"/> and the pod fails its startup probe
/// (<see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigLoadedHealthCheck"/>, ADR-0018).</para>
/// <para>A change reaches all replicas within one poll interval. The gauge <c>superapp.gateway.proxy_config.loaded</c> reports the active migration
/// per replica. Registered as a singleton that is at the same time the YARP provider and a hosted service (see <c>Program.cs</c>).</para>
/// </remarks>
/// <param name="scopeFactory">Creates a scope per poll to resolve a <see cref="SuperApp.Gateway.Persistence.GatewayDbContext"/>.</param>
/// <param name="validator">YARP validator for routes and clusters.</param>
/// <param name="cache">Two-level cache holding the last known good snapshot.</param>
/// <param name="configuration">Application configuration: <c>Gateway:Profile</c> (default <c>bff-web</c>) and <c>Gateway:ConfigPollInterval</c>.</param>
/// <param name="logger">Logger for applied, rejected and failed reloads.</param>
internal sealed partial class DatabaseProxyConfigProvider(
    IServiceScopeFactory scopeFactory,
    IConfigValidator validator,
    HybridCache cache,
    IConfiguration configuration,
    ILogger<DatabaseProxyConfigProvider> logger) : BackgroundService, IProxyConfigProvider
{
    private readonly string _profile = configuration["Gateway:Profile"] ?? GatewayProfiles.BffWeb;
    private readonly TimeSpan _pollInterval = configuration.GetValue("Gateway:ConfigPollInterval", TimeSpan.FromSeconds(5));
    private volatile ActiveConfig _current = ActiveConfig.Empty;
    private volatile string? _rejectedMigrationId;

    /// <summary>
    /// Whether a validated configuration (from the database or the cache) is active. Condition of the gateway's startup probe (ADR-0018);
    /// until it is <see langword="true"/> YARP has no routes.
    /// </summary>
    public bool IsLoaded => _current.MigrationId is not null;

    /// <summary>Id of the gateway migration the active configuration was read at, or <see langword="null"/> when nothing is loaded yet.</summary>
    public string? ActiveMigrationId => _current.MigrationId;

    /// <summary>
    /// Id of the last gateway migration whose configuration was read from the database and rejected (event 3002), or <see langword="null"/>
    /// when none was rejected since the last successful reload. Polls skip this migration id until a newer migration is applied.
    /// </summary>
    public string? RejectedMigrationId => _rejectedMigrationId;

    private string CacheKey => $"gateway:yarp-config:v1:{_profile}";

    /// <summary>Returns the active configuration; YARP subscribes to its change token to learn about the next reload.</summary>
    /// <returns>The active routes and clusters; empty until the first configuration has been loaded.</returns>
    public IProxyConfig GetConfig() => _current;

    /// <summary>
    /// Loads the initial configuration (from the database, or from the cache when the database is unavailable) and then polls for new
    /// migrations until the host stops, as described on <see cref="SuperApp.Gateway.Proxy.Configuration.DatabaseProxyConfigProvider"/>.
    /// </summary>
    /// <param name="stoppingToken">Signalled when the host shuts down.</param>
    /// <returns>A task that completes when the host stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        GatewayTelemetry.Meter.CreateObservableGauge(
            "superapp.gateway.proxy_config.loaded",
            () => new Measurement<int>(
                IsLoaded ? 1 : 0,
                new KeyValuePair<string, object?>("profile", _profile),
                new KeyValuePair<string, object?>("migration_id", ActiveMigrationId)),
            description: "Czy replika ma załadowaną konfigurację tras (z identyfikatorem migracji)");

        await LoadFromCacheIfDatabaseUnavailableAsync(stoppingToken);

        using var timer = new PeriodicTimer(_pollInterval);
        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                GatewayTelemetry.ProxyConfigReloadFailures.Add(1);
                LogRefreshFailed(logger, _profile, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // One poll: compare the last applied migration with the active one and with the last rejected one; on a new version read, validate,
    // apply and cache the configuration, or remember the version as rejected so it is logged and counted only once.
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var migrationId = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
        if (migrationId is null || migrationId == _current.MigrationId || migrationId == _rejectedMigrationId)
        {
            return;
        }

        var snapshot = await ReadSnapshotAsync(db, migrationId, cancellationToken);
        if (await ApplyAsync(snapshot, cancellationToken))
        {
            _rejectedMigrationId = null;
            await cache.SetAsync(CacheKey, snapshot, new HybridCacheEntryOptions { Expiration = TimeSpan.FromDays(30) }, cancellationToken: cancellationToken);
        }
        else
        {
            _rejectedMigrationId = migrationId;
        }
    }

    // Startup: try the database first; only if that throws, fall back to the last known good snapshot in the cache
    // (read-only: DisableUnderlyingData means the factory is never called and nothing is written).
    private async Task LoadFromCacheIfDatabaseUnavailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRefreshFailed(logger, _profile, exception);
        }

        var cached = await cache.GetOrCreateAsync<ProxyConfigSnapshot?>(
            CacheKey,
            _ => ValueTask.FromResult<ProxyConfigSnapshot?>(null),
            new HybridCacheEntryOptions { Flags = HybridCacheEntryFlags.DisableUnderlyingData },
            cancellationToken: cancellationToken);

        if (cached is not null && await ApplyAsync(cached, cancellationToken))
        {
            LogLoadedFromCache(logger, _profile, cached.MigrationId);
        }
    }

    private async Task<ProxyConfigSnapshot> ReadSnapshotAsync(GatewayDbContext db, string migrationId, CancellationToken cancellationToken) =>
        new(
            migrationId,
            await db.Clusters.AsNoTracking().Where(row => row.Profile == _profile).ToListAsync(cancellationToken),
            await db.Destinations.AsNoTracking().Where(row => row.Profile == _profile).ToListAsync(cancellationToken),
            await db.Routes.AsNoTracking().Where(row => row.Profile == _profile).ToListAsync(cancellationToken),
            await db.RouteMethods.AsNoTracking().Where(row => row.Profile == _profile).ToListAsync(cancellationToken),
            await db.RouteHosts.AsNoTracking().Where(row => row.Profile == _profile).ToListAsync(cancellationToken),
            await db.RouteTransforms.AsNoTracking().Where(row => row.Profile == _profile).ToListAsync(cancellationToken));

    // Maps and validates the whole snapshot and swaps it in atomically; returns false (and keeps the current configuration) on any mapping
    // or validation error.
    private async Task<bool> ApplyAsync(ProxyConfigSnapshot snapshot, CancellationToken cancellationToken)
    {
        var mapping = ProxyConfigMapper.Map(snapshot);
        var routes = mapping.Routes;
        var clusters = mapping.Clusters;

        var errors = mapping.Errors.Select(error => (Exception)new InvalidOperationException(error)).ToList();
        foreach (var route in routes)
        {
            errors.AddRange(await validator.ValidateRouteAsync(route));
        }

        foreach (var cluster in clusters)
        {
            errors.AddRange(await validator.ValidateClusterAsync(cluster));
        }

        if (errors.Count > 0)
        {
            GatewayTelemetry.ProxyConfigReloadFailures.Add(1);
            LogInvalidConfig(logger, _profile, snapshot.MigrationId, new AggregateException(errors));
            return false;
        }

        var previous = _current;
        _current = new ActiveConfig(snapshot.MigrationId, routes, clusters);
        previous.SignalChange();
        LogApplied(logger, _profile, snapshot.MigrationId, routes.Count, clusters.Count);
        return true;
    }

    [LoggerMessage(3001, LogLevel.Information, "Gateway {Profile}: applied proxy config {MigrationId} ({RouteCount} routes, {ClusterCount} clusters)")]
    private static partial void LogApplied(ILogger logger, string profile, string migrationId, int routeCount, int clusterCount);

    [LoggerMessage(3002, LogLevel.Error, "Gateway {Profile}: proxy config {MigrationId} rejected by validation; keeping previous configuration")]
    private static partial void LogInvalidConfig(ILogger logger, string profile, string migrationId, Exception exception);

    [LoggerMessage(3003, LogLevel.Warning, "Gateway {Profile}: proxy config refresh failed; keeping current configuration")]
    private static partial void LogRefreshFailed(ILogger logger, string profile, Exception exception);

    [LoggerMessage(3004, LogLevel.Warning, "Gateway {Profile}: database unavailable at startup; loaded last valid config {MigrationId} from cache")]
    private static partial void LogLoadedFromCache(ILogger logger, string profile, string migrationId);

    /// <summary>
    /// Immutable YARP configuration version. Its change token is cancelled when a newer version replaces it, which tells YARP to call
    /// <see cref="GetConfig"/> again.
    /// </summary>
    private sealed class ActiveConfig : IProxyConfig
    {
        private readonly CancellationTokenSource _changeSource = new();

        public ActiveConfig(string? migrationId, IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        {
            MigrationId = migrationId;
            Routes = routes;
            Clusters = clusters;
            ChangeToken = new CancellationChangeToken(_changeSource.Token);
        }

        /// <summary>The configuration before the first load: no routes, no clusters, no migration id.</summary>
        public static ActiveConfig Empty { get; } = new(null, [], []);

        /// <summary>Gateway migration this configuration was read at; <see langword="null"/> only for <see cref="Empty"/>.</summary>
        public string? MigrationId { get; }

        /// <inheritdoc />
        public IReadOnlyList<RouteConfig> Routes { get; }

        /// <inheritdoc />
        public IReadOnlyList<ClusterConfig> Clusters { get; }

        /// <inheritdoc />
        public IChangeToken ChangeToken { get; }

        /// <summary>Signals YARP that this configuration has been replaced.</summary>
        public void SignalChange() => _changeSource.Cancel();
    }
}
