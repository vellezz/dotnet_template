using SuperApp.Gateway.Persistence.Entities;
using SuperApp.Gateway.Persistence.Seed;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SuperApp.Gateway.Persistence;

/// <summary>
/// EF Core context of the gateway, owner of the <c>gateway</c> schema: BFF sessions and Data Protection keys (ADR-0013) and the YARP route
/// configuration tables (ADR-0022). The gateway is not a domain service, so this is a plain <see cref="DbContext"/> without aggregates,
/// domain events or a read/write split.
/// </summary>
/// <remarks>
/// <para>
/// Its migrations live in <c>Persistence/Migrations</c> and are applied by <c>SuperApp.Migrator</c> (dev/test) or by the DBA (prod), never
/// by the gateway itself (ADR-0004); at startup the gateway only checks that none are pending. Route configuration is part of the model
/// (<see cref="SuperApp.Gateway.Persistence.Seed.ProxyConfigurationSeed"/>, <c>HasData</c>), so a route change is a migration, and every applied migration is a new
/// configuration version for <see cref="SuperApp.Gateway.Proxy.Configuration.DatabaseProxyConfigProvider"/>.
/// </para>
/// <para>
/// Each profile deployment uses the same schema; the route tables contain the rows of both profiles, separated by the <c>Profile</c> column.
/// </para>
/// </remarks>
/// <param name="options">Context options, configured by <see cref="SuperApp.Gateway.Persistence.GatewayDbContextOptions.Configure"/>.</param>
public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>Database schema of the gateway; also holds the migration history table <c>__EFMigrationsHistory</c> (ADR-0021).</summary>
    public const string SchemaName = "gateway";

    /// <summary>Data Protection key ring shared by all replicas of the <c>bff-web</c> profile (ADR-0013); managed by Data Protection itself.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    internal DbSet<Session> Sessions => Set<Session>();

    /// <summary>YARP clusters per profile (ADR-0022). Read-only at runtime; changed only through migrations.</summary>
    public DbSet<ProxyCluster> Clusters => Set<ProxyCluster>();

    /// <summary>Destination addresses of the YARP clusters (ADR-0022). Read-only at runtime; changed only through migrations.</summary>
    public DbSet<ProxyDestination> Destinations => Set<ProxyDestination>();

    /// <summary>YARP routes per profile (ADR-0022). Read-only at runtime; changed only through migrations.</summary>
    public DbSet<ProxyRoute> Routes => Set<ProxyRoute>();

    /// <summary>HTTP methods matched by routes; a route without rows matches any method.</summary>
    public DbSet<ProxyRouteMethod> RouteMethods => Set<ProxyRouteMethod>();

    /// <summary>Hosts matched by routes; a route without rows matches any host.</summary>
    public DbSet<ProxyRouteHost> RouteHosts => Set<ProxyRouteHost>();

    /// <summary>Request and response transforms of routes, applied in ascending <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRouteTransform.Order"/>.</summary>
    public DbSet<ProxyRouteTransform> RouteTransforms => Set<ProxyRouteTransform>();

    // The synchronous SaveChanges stays available: the EF-based Data Protection key repository saves synchronously.
    // The gateway's own code saves asynchronously; APP003 enforces that (ADR-0027 concerns the Unit of Work of aggregates).

    /// <summary>
    /// Sets the default schema <see cref="SchemaName"/>, applies the <c>IEntityTypeConfiguration</c> classes of this assembly and the route
    /// and cluster data (<c>HasData</c>) from which the YARP configuration migrations are generated (ADR-0022).
    /// </summary>
    /// <param name="modelBuilder">The EF Core model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GatewayDbContext).Assembly);
        ProxyConfigurationSeed.Apply(modelBuilder);
    }
}
