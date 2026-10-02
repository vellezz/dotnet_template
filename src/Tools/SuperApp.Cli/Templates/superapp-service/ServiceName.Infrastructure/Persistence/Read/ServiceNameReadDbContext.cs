using System.Reflection;
using SuperApp.Framework.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ServiceName.Domain;
using ServiceName.Infrastructure.Persistence.Write;

namespace ServiceName.Infrastructure.Persistence.Read;

/// <summary>
/// Read database context of the ServiceName service: maps flat read models for queries, without change tracking, saving or migrations
/// (ADR-0003, ADR-0026).
/// </summary>
/// <remarks>
/// <para>
/// Query handlers (they live in Infrastructure, next to this context) project directly into DTOs from the read models exposed here; they
/// never load aggregates or use repositories. Read models are plain classes shaped for a screen or endpoint, mapped to the tables created by
/// the write side or to views, never to domain entities.
/// </para>
/// <para>
/// TODO when adding a query: add the read model class, its <c>IEntityTypeConfiguration&lt;T&gt;</c> in this namespace (only configurations
/// from this namespace are applied) and an <c>internal IQueryable&lt;T&gt;</c> property here. Schema changes go through the write context's
/// migrations, never through this one.
/// </para>
/// </remarks>
/// <param name="options">Context options from DI (<c>Read</c> connection string, no-tracking queries).</param>
public sealed class ServiceNameReadDbContext(DbContextOptions<ServiceNameReadDbContext> options) : ReadDbContextBase(options)
{
    /// <inheritdoc />
    protected override string Schema => ServiceNameWriteDbContext.SchemaName;

    /// <inheritdoc />
    protected override Assembly DomainAssembly => ServiceNameDomain.Assembly;
}
