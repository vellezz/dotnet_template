using SuperApp.Gateway.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SuperApp.Gateway.Persistence.Configurations;

/// <summary>
/// EF mapping of <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRoute"/> to the temporal table <c>gateway.Routes</c>: CHECK constraints on profile, path and limits,
/// a required authorization policy, a unique (Profile, Path, Order) index and the FK to <c>gateway.Clusters</c> (ADR-0022).
/// </summary>
/// <remarks>
/// The FK to the cluster uses <c>NO ACTION</c> on delete: a cluster that is still used by a route cannot be deleted, so a migration must
/// remove or re-point the routes first (unlike destinations, which are deleted together with their cluster).
/// </remarks>
internal sealed class ProxyRouteConfiguration : IEntityTypeConfiguration<ProxyRoute>
{
    /// <summary>Configures the table, its CHECK constraints, keys, index, column lengths and the FK to the cluster.</summary>
    /// <param name="builder">Builder of the <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRoute"/> entity.</param>
    public void Configure(EntityTypeBuilder<ProxyRoute> builder)
    {
        builder.ToTable("Routes", table =>
        {
            table.IsTemporal();
            table.HasCheckConstraint("CK_Routes_Profile", ProxyChecks.Profile);
            table.HasCheckConstraint("CK_Routes_Path", "[Path] LIKE '/%'");
            table.HasCheckConstraint("CK_Routes_Limits",
                "([TimeoutSeconds] IS NULL OR [TimeoutSeconds] > 0) AND ([MaxRequestBodySize] IS NULL OR [MaxRequestBodySize] > 0)");
        });
        builder.HasKey(route => new { route.Profile, route.RouteId });
        builder.Property(route => route.Profile).HasMaxLength(20);
        builder.Property(route => route.RouteId).HasMaxLength(100);
        builder.Property(route => route.ClusterId).HasMaxLength(100);
        builder.Property(route => route.Path).HasMaxLength(500);
        builder.Property(route => route.AuthorizationPolicy).HasMaxLength(100).IsRequired();
        builder.Property(route => route.RateLimiterPolicy).HasMaxLength(100);
        builder.HasIndex(route => new { route.Profile, route.Path, route.Order }).IsUnique();
        builder.HasOne<ProxyCluster>().WithMany().HasForeignKey(route => new { route.Profile, route.ClusterId }).OnDelete(DeleteBehavior.NoAction);
    }
}
