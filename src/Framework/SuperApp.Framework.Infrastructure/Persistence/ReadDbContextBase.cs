using SuperApp.Framework.Infrastructure.Persistence.Conventions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace SuperApp.Framework.Infrastructure.Persistence;

/// <summary>
/// Base class of a service's read database context: maps read models shaped for queries, never tracks changes, never saves and never
/// creates migrations (ADR-0003, ADR-0026).
/// </summary>
/// <remarks>
/// <para>
/// Read models are plain classes in the Infrastructure project (<c>Persistence/Read</c>), mapped to the tables created by the write side or
/// to views, with only the columns the queries need. Query handlers use the derived context directly.
/// </para>
/// <para>
/// Only <c>IEntityTypeConfiguration&lt;T&gt;</c> classes in the derived context's namespace or its child namespaces (by convention
/// <c>Persistence/Read/Configurations</c>) are applied, which keeps read model
/// mappings separate from the write model mappings in the same assembly.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class KnowledgeReadDbContext(DbContextOptions&lt;KnowledgeReadDbContext&gt; options) : ReadDbContextBase(options)
/// {
///     internal IQueryable&lt;CategoryRow&gt; Categories =&gt; Set&lt;CategoryRow&gt;();
///
///     protected override string Schema =&gt; KnowledgeWriteDbContext.SchemaName;
///
///     protected override Assembly DomainAssembly =&gt; KnowledgeDomain.Assembly;
/// }
/// </code>
/// </example>
public abstract class ReadDbContextBase : DbContext
{
    /// <summary>Initializes the context with change tracking disabled for every query.</summary>
    /// <param name="options">Context options configured by <c>AddAppPersistence</c> (the <c>Read</c> connection string).</param>
    protected ReadDbContextBase(DbContextOptions options)
        : base(options) => ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

    /// <summary>Gets the service schema; the same value as the write context's schema, because both work on the same tables.</summary>
    protected abstract string Schema { get; }

    /// <summary>Gets the domain assembly whose strongly typed IDs and value objects get automatic value conversions (usually <c>{Service}Domain.Assembly</c>).</summary>
    protected abstract Assembly DomainAssembly { get; }

    /// <summary>Not supported: the read context never saves.</summary>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override int SaveChanges() => throw ReadOnly();

    /// <summary>Not supported: the read context never saves.</summary>
    /// <param name="acceptAllChangesOnSuccess">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();

    /// <summary>Not supported: the read context never saves.</summary>
    /// <param name="acceptAllChangesOnSuccess">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    /// <summary>Sets the default schema and applies the entity configurations declared in the derived context's namespace or its child namespaces.</summary>
    /// <param name="modelBuilder">The EF Core model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly, IsInContextNamespace);
    }

    /// <summary>Adds value conversions for all strongly typed IDs and single-value objects of <see cref="DomainAssembly"/> (ADR-0023, ADR-0024).</summary>
    /// <param name="configurationBuilder">The EF Core conventions builder.</param>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddSingleValueObjectConversions(DomainAssembly);

    private bool IsInContextNamespace(Type configurationType) =>
        configurationType.Namespace is { } ns
        && (ns == GetType().Namespace || ns.StartsWith(GetType().Namespace + ".", StringComparison.Ordinal));

    private static NotSupportedException ReadOnly() => new("ReadDbContext służy wyłącznie do odczytu (ADR-0003).");
}
