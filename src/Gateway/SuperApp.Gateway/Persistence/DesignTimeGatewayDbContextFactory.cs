using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SuperApp.Gateway.Persistence;

/// <summary>
/// Creates <see cref="SuperApp.Gateway.Persistence.GatewayDbContext"/> for the <c>dotnet ef</c> tools, so gateway migrations can be generated without starting the gateway
/// or having a database (ADR-0004).
/// </summary>
/// <remarks>
/// The connection string is a placeholder and is never opened when adding a migration or generating a script. Migrations are applied by
/// <c>SuperApp.Migrator</c> (dev/test) or by the DBA (prod), never by <c>dotnet ef database update</c> against a shared environment.
/// </remarks>
/// <example>
/// <code>
/// dotnet ef migrations add AddOrdersRoutes -p src/Gateway/SuperApp.Gateway
/// dotnet ef migrations script --idempotent -p src/Gateway/SuperApp.Gateway
/// </code>
/// </example>
internal sealed class DesignTimeGatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    /// <summary>Creates the context with the same SQL Server options as at runtime (<see cref="SuperApp.Gateway.Persistence.GatewayDbContextOptions.Configure"/>).</summary>
    /// <param name="args">Arguments passed by the EF tools; not used.</param>
    /// <returns>A context for design-time operations only.</returns>
    public GatewayDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<GatewayDbContext>();
        GatewayDbContextOptions.Configure(builder, "Server=design-time;Database=SuperApp;Integrated Security=true");
        return new GatewayDbContext(builder.Options);
    }
}
