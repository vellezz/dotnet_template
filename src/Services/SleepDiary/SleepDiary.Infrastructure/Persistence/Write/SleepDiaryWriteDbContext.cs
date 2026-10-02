using SuperApp.Framework.Application.Events;
using System.Reflection;
using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SleepDiary.Domain;
using SleepDiary.Domain.Entries;
using SleepDiary.Infrastructure.Persistence.Write.Configurations;

namespace SleepDiary.Infrastructure.Persistence.Write;

/// <summary>
/// Write-side <c>DbContext</c> of the SleepDiary service: persists the <see cref="Domain.Entries.SleepEntry"/> aggregates and the MassTransit
/// outbox/inbox, and is the unit of work of every command (ADR-0003, ADR-0027).
/// </summary>
/// <remarks>
/// <para>
/// It is the only owner of the <see cref="SchemaName"/> schema and its migrations (stored in <c>Migrations/</c> of this project, applied by
/// <c>SuperApp.Migrator</c>). Every change to the write model requires a new expand/contract migration; never edit an applied one.
/// </para>
/// <para>
/// On save, the base class collects domain events from the tracked aggregates and dispatches them to their handlers before writing, so the
/// aggregate changes and the outbox messages produced by those handlers are committed atomically. Command handlers never call save
/// themselves; the transaction pipeline behavior does.
/// </para>
/// <para>
/// A unique index violation does not throw from <c>IUnitOfWork.SaveChangesAsync</c>; it is returned as a Conflict error.
/// <see cref="UniqueConstraintErrors"/> maps the index that users can hit through a race (two concurrent requests recording the same day)
/// to the same error the handler returns when it detects the duplicate itself.
/// </para>
/// <para>
/// Entity configurations are picked up automatically from this context's namespace (<c>SleepDiary.Infrastructure.Persistence.Write</c>).
/// </para>
/// </remarks>
/// <param name="options">Context options configured by <c>AddAppPersistence</c> (write-side connection string, migrations history table in the service schema).</param>
/// <param name="dispatcher">Dispatcher of domain events, invoked during <c>SaveChangesAsync</c> in the same transaction (ADR-0027).</param>
public sealed class SleepDiaryWriteDbContext(DbContextOptions<SleepDiaryWriteDbContext> options, IDomainEventDispatcher dispatcher)
    : WriteDbContextBase(options, dispatcher)
{
    /// <summary>
    /// Name of the service's schema in the shared MSSQL database (<c>sleepdiary</c>): tables, outbox, inbox and migrations history (ADR-0021).
    /// Also used as the Redis key prefix and the queue name prefix of the service.
    /// </summary>
    public const string SchemaName = "sleepdiary";

    /// <inheritdoc />
    protected override string Schema => SchemaName;

    /// <inheritdoc />
    protected override Assembly DomainAssembly => SleepDiaryDomain.Assembly;

    /// <summary>
    /// Maps the unique index on <c>UserId</c> + <c>Date</c> (<see cref="SleepEntryConfiguration.UserDateIndexName"/>) to
    /// <see cref="SleepEntryErrors.AlreadyExists"/> (<c>sleepdiary.entry.already_exists</c>, HTTP 409).
    /// </summary>
    /// <remarks>
    /// <c>RecordSleepEntryHandler</c> checks for an existing entry before inserting, but the check and the insert are not atomic: two concurrent
    /// requests for the same user and day can both pass it. The index then rejects the second insert and this mapping turns the violation into
    /// the same error the handler would have returned, instead of a generic <c>persistence.duplicate</c> or an HTTP 500.
    /// </remarks>
    protected override IReadOnlyDictionary<string, Error> UniqueConstraintErrors { get; } = new Dictionary<string, Error>
    {
        [SleepEntryConfiguration.UserDateIndexName] = SleepEntryErrors.AlreadyExists,
    };
}
