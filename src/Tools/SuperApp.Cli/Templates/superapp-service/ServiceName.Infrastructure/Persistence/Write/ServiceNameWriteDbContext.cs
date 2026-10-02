using SuperApp.Framework.Application.Events;
using System.Reflection;
using SuperApp.Framework.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ServiceName.Domain;

namespace ServiceName.Infrastructure.Persistence.Write;

/// <summary>
/// Write database context of the ServiceName service: persists aggregates, owns the <c>servicename</c> schema and its migrations, and
/// is the unit of work of every command (ADR-0003, ADR-0021).
/// </summary>
/// <remarks>
/// <para>
/// Everything generic comes from <see cref="WriteDbContextBase"/>: <c>IUnitOfWork</c>, domain event dispatch in <c>SaveChangesAsync</c>
/// within the same transaction (ADR-0027), MassTransit outbox and inbox tables, and automatic conversions for IDs and single-value
/// objects from <see cref="ServiceNameDomain"/>. Command handlers never use this class directly; they use repositories and <c>IUnitOfWork</c>.
/// </para>
/// <para>
/// TODO when adding an aggregate: put its <c>IEntityTypeConfiguration&lt;T&gt;</c> (backing fields, <c>ComplexProperty</c> for multi-value
/// objects, <c>OwnsMany</c> for collections of value objects, no EF attributes in Domain) and its repository in this namespace. Only
/// configurations from this namespace are applied. Then add a migration (see <see cref="DesignTimeWriteDbContextFactory"/>).
/// </para>
/// </remarks>
/// <param name="options">Context options from DI (<c>Write</c> connection string, migration history in the service schema).</param>
/// <param name="dispatcher">Dispatches domain events to their handlers inside <c>SaveChangesAsync</c>, in the same transaction (ADR-0027).</param>
public sealed class ServiceNameWriteDbContext(DbContextOptions<ServiceNameWriteDbContext> options, IDomainEventDispatcher dispatcher)
    : WriteDbContextBase(options, dispatcher)
{
    /// <summary>
    /// MSSQL schema of the service (<c>servicename</c>): its tables, migration history, outbox and inbox (ADR-0021). The same value prefixes
    /// the RabbitMQ queue names and the Redis cache keys of the service.
    /// </summary>
    public const string SchemaName = "servicename";

    /// <inheritdoc />
    protected override string Schema => SchemaName;

    /// <inheritdoc />
    protected override Assembly DomainAssembly => ServiceNameDomain.Assembly;
}
