using SleepDiary.Infrastructure.Persistence.Read.Models;
using System.Reflection;
using SuperApp.Framework.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SleepDiary.Domain;
using SleepDiary.Infrastructure.Persistence.Write;

namespace SleepDiary.Infrastructure.Persistence.Read;

/// <summary>
/// Read-side <c>DbContext</c> of the SleepDiary service: maps flat read models onto the tables owned by <see cref="SleepDiaryWriteDbContext"/>,
/// for use by query handlers only (ADR-0003, ADR-0026).
/// </summary>
/// <remarks>
/// <para>
/// It never tracks entities, never saves and has no migrations; the schema is defined solely by the write context. It maps its own read model
/// classes (<see cref="SleepEntryRow"/>), never domain entities. It uses the separate <c>Read</c> connection string.
/// </para>
/// <para>
/// Entity configurations are picked up automatically from this context's namespace (<c>SleepDiary.Infrastructure.Persistence.Read</c>);
/// put new read model configurations there.
/// </para>
/// </remarks>
/// <param name="options">Context options configured by <c>AddAppPersistence</c> (read-side connection string, no tracking).</param>
public sealed class SleepDiaryReadDbContext(DbContextOptions<SleepDiaryReadDbContext> options) : ReadDbContextBase(options)
{
    /// <summary>Gets the diary entries of all users; query handlers must always filter by the caller's <c>sub</c>.</summary>
    internal IQueryable<SleepEntryRow> Entries => Set<SleepEntryRow>();

    /// <inheritdoc />
    protected override string Schema => SleepDiaryWriteDbContext.SchemaName;

    /// <inheritdoc />
    protected override Assembly DomainAssembly => SleepDiaryDomain.Assembly;
}
