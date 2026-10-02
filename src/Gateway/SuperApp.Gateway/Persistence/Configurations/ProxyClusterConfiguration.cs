using SuperApp.Gateway.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SuperApp.Gateway.Persistence.Configurations;

/// <summary>
/// EF mapping of <see cref="SuperApp.Gateway.Persistence.Entities.ProxyCluster"/> to the temporal table <c>gateway.Clusters</c>. Keys, foreign keys and CHECK constraints reject
/// invalid data already when a migration writes it; the temporal history shows every change, including forbidden manual edits (ADR-0022).
/// </summary>
/// <remarks>
/// This is the first of two validation levels: the database checks the shape of the data (allowed values, positive timeouts, health check path
/// present when enabled); the YARP validator in <see cref="SuperApp.Gateway.Proxy.Configuration.DatabaseProxyConfigProvider"/> then checks consistency with the code
/// (for example that a referenced policy exists).
/// </remarks>
internal sealed class ProxyClusterConfiguration : IEntityTypeConfiguration<ProxyCluster>
{
    /// <summary>Configures the table, its CHECK constraints, the composite key and the column lengths.</summary>
    /// <param name="builder">Builder of the <see cref="SuperApp.Gateway.Persistence.Entities.ProxyCluster"/> entity.</param>
    public void Configure(EntityTypeBuilder<ProxyCluster> builder)
    {
        builder.ToTable("Clusters", table =>
        {
            table.IsTemporal();
            table.HasCheckConstraint("CK_Clusters_Profile", ProxyChecks.Profile);
            table.HasCheckConstraint("CK_Clusters_LoadBalancing",
                "[LoadBalancingPolicy] IS NULL OR [LoadBalancingPolicy] IN ('RoundRobin','PowerOfTwoChoices','LeastRequests','Random','FirstAlphabetical')");
            table.HasCheckConstraint("CK_Clusters_HttpVersion", "[HttpVersion] IS NULL OR [HttpVersion] IN ('1.1','2')");
            table.HasCheckConstraint("CK_Clusters_Timeouts",
                "[ActivityTimeoutSeconds] > 0 AND ([HealthCheckIntervalSeconds] IS NULL OR [HealthCheckIntervalSeconds] > 0) AND ([HealthCheckTimeoutSeconds] IS NULL OR [HealthCheckTimeoutSeconds] > 0)");
            table.HasCheckConstraint("CK_Clusters_HealthCheck", "[HealthCheckEnabled] = 0 OR [HealthCheckPath] IS NOT NULL");
        });
        builder.HasKey(cluster => new { cluster.Profile, cluster.ClusterId });
        builder.Property(cluster => cluster.Profile).HasMaxLength(20);
        builder.Property(cluster => cluster.ClusterId).HasMaxLength(100);
        builder.Property(cluster => cluster.LoadBalancingPolicy).HasMaxLength(30);
        builder.Property(cluster => cluster.HttpVersion).HasMaxLength(5);
        builder.Property(cluster => cluster.HealthCheckPath).HasMaxLength(200);
    }
}
