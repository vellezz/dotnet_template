using SuperApp.Gateway.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SuperApp.Gateway.Persistence.Configurations;

/// <summary>EF mapping of <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRouteHost"/> to the temporal table <c>gateway.RouteHosts</c> with the FK to <c>gateway.Routes</c> (ADR-0022).</summary>
internal sealed class ProxyRouteHostConfiguration : IEntityTypeConfiguration<ProxyRouteHost>
{
    /// <summary>Configures the table, the composite key, the column lengths and the FK to the route.</summary>
    /// <param name="builder">Builder of the <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRouteHost"/> entity.</param>
    public void Configure(EntityTypeBuilder<ProxyRouteHost> builder)
    {
        builder.ToTable("RouteHosts", table => table.IsTemporal());
        builder.HasKey(host => new { host.Profile, host.RouteId, host.Host });
        builder.Property(host => host.Profile).HasMaxLength(20);
        builder.Property(host => host.RouteId).HasMaxLength(100);
        builder.Property(host => host.Host).HasMaxLength(255);
        builder.HasOne<ProxyRoute>().WithMany().HasForeignKey(host => new { host.Profile, host.RouteId });
    }
}
