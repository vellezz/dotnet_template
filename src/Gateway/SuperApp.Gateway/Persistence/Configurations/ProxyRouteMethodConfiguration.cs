using SuperApp.Gateway.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SuperApp.Gateway.Persistence.Configurations;

/// <summary>
/// EF mapping of <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRouteMethod"/> to the temporal table <c>gateway.RouteMethods</c>, with the CHECK constraint on allowed
/// HTTP methods and the FK to <c>gateway.Routes</c> (ADR-0022).
/// </summary>
internal sealed class ProxyRouteMethodConfiguration : IEntityTypeConfiguration<ProxyRouteMethod>
{
    /// <summary>Configures the table, its CHECK constraint, the composite key, the column lengths and the FK to the route.</summary>
    /// <param name="builder">Builder of the <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRouteMethod"/> entity.</param>
    public void Configure(EntityTypeBuilder<ProxyRouteMethod> builder)
    {
        builder.ToTable("RouteMethods", table =>
        {
            table.IsTemporal();
            table.HasCheckConstraint("CK_RouteMethods_Method", "[Method] IN ('GET','POST','PUT','PATCH','DELETE','HEAD','OPTIONS')");
        });
        builder.HasKey(method => new { method.Profile, method.RouteId, method.Method });
        builder.Property(method => method.Profile).HasMaxLength(20);
        builder.Property(method => method.RouteId).HasMaxLength(100);
        builder.Property(method => method.Method).HasMaxLength(10);
        builder.HasOne<ProxyRoute>().WithMany().HasForeignKey(method => new { method.Profile, method.RouteId });
    }
}
