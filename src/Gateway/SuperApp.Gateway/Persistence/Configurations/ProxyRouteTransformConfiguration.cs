using SuperApp.Gateway.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SuperApp.Gateway.Persistence.Configurations;

/// <summary>
/// EF mapping of <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRouteTransform"/> to the temporal table <c>gateway.RouteTransforms</c>, with CHECK constraints on the
/// supported kinds and on which of <c>Name</c>/<c>Value</c> each kind requires, and the FK to <c>gateway.Routes</c> (ADR-0022).
/// </summary>
/// <remarks>
/// Supporting a new transform kind requires changing both constraints here (in a migration) and <see cref="SuperApp.Gateway.Proxy.Configuration.ProxyConfigMapper"/>.
/// </remarks>
internal sealed class ProxyRouteTransformConfiguration : IEntityTypeConfiguration<ProxyRouteTransform>
{
    /// <summary>Configures the table, its CHECK constraints, the composite key, the column lengths and the FK to the route.</summary>
    /// <param name="builder">Builder of the <see cref="SuperApp.Gateway.Persistence.Entities.ProxyRouteTransform"/> entity.</param>
    public void Configure(EntityTypeBuilder<ProxyRouteTransform> builder)
    {
        builder.ToTable("RouteTransforms", table =>
        {
            table.IsTemporal();
            table.HasCheckConstraint("CK_RouteTransforms_Kind",
                "[Kind] IN ('PathRemovePrefix','PathPrefix','PathPattern','RequestHeaderSet','RequestHeaderRemove','ResponseHeaderSet','ResponseHeaderRemove')");
            table.HasCheckConstraint("CK_RouteTransforms_Fields",
                "([Kind] IN ('PathRemovePrefix','PathPrefix','PathPattern') AND [Value] IS NOT NULL AND [Name] IS NULL)"
                + " OR ([Kind] IN ('RequestHeaderSet','ResponseHeaderSet') AND [Name] IS NOT NULL AND [Value] IS NOT NULL)"
                + " OR ([Kind] IN ('RequestHeaderRemove','ResponseHeaderRemove') AND [Name] IS NOT NULL AND [Value] IS NULL)");
        });
        builder.HasKey(transform => new { transform.Profile, transform.RouteId, transform.Order });
        builder.Property(transform => transform.Profile).HasMaxLength(20);
        builder.Property(transform => transform.RouteId).HasMaxLength(100);
        builder.Property(transform => transform.Kind).HasMaxLength(30);
        builder.Property(transform => transform.Name).HasMaxLength(100);
        builder.Property(transform => transform.Value).HasMaxLength(500);
        builder.HasOne<ProxyRoute>().WithMany().HasForeignKey(transform => new { transform.Profile, transform.RouteId });
    }
}
