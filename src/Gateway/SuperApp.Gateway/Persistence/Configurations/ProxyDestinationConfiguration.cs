using SuperApp.Gateway.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SuperApp.Gateway.Persistence.Configurations;

/// <summary>
/// EF mapping of <see cref="SuperApp.Gateway.Persistence.Entities.ProxyDestination"/> to the temporal table <c>gateway.Destinations</c>, with the CHECK constraint that restricts
/// addresses to in-cluster Kubernetes Services (<see cref="ProxyChecks.ClusterAddress"/>) and the FK to <c>gateway.Clusters</c> (ADR-0022).
/// </summary>
internal sealed class ProxyDestinationConfiguration : IEntityTypeConfiguration<ProxyDestination>
{
    /// <summary>Configures the table, its CHECK constraints, the composite key, the column lengths and the FK to the cluster.</summary>
    /// <param name="builder">Builder of the <see cref="SuperApp.Gateway.Persistence.Entities.ProxyDestination"/> entity.</param>
    public void Configure(EntityTypeBuilder<ProxyDestination> builder)
    {
        builder.ToTable("Destinations", table =>
        {
            table.IsTemporal();
            table.HasCheckConstraint("CK_Destinations_Profile", ProxyChecks.Profile);
            table.HasCheckConstraint("CK_Destinations_ClusterAddress", ProxyChecks.ClusterAddress);
        });
        builder.HasKey(destination => new { destination.Profile, destination.ClusterId, destination.DestinationId });
        builder.Property(destination => destination.Profile).HasMaxLength(20);
        builder.Property(destination => destination.ClusterId).HasMaxLength(100);
        builder.Property(destination => destination.DestinationId).HasMaxLength(100);
        builder.Property(destination => destination.Address).HasMaxLength(500);
        builder.HasOne<ProxyCluster>().WithMany().HasForeignKey(destination => new { destination.Profile, destination.ClusterId });
    }
}
