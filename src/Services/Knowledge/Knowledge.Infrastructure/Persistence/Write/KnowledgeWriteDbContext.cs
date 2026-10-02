using SuperApp.Framework.Application.Events;
using System.Reflection;
using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Knowledge.Domain;
using Knowledge.Domain.Categories;
using Knowledge.Domain.Library;

namespace Knowledge.Infrastructure.Persistence.Write;

/// <summary>
/// Write context of the Knowledge service: persists the aggregates and the MassTransit outbox/inbox, implements the unit of work and is
/// the only owner of the <c>knowledge</c> schema and its migrations (ADR-0003, ADR-0021).
/// </summary>
/// <remarks>
/// <para>
/// Used only through repositories and the unit of work by command handlers, never by queries. <c>SaveChangesAsync</c> (implemented in
/// <c>WriteDbContextBase</c>) collects the domain events of tracked aggregates, dispatches them to the domain event handlers and then saves
/// everything, including outbox messages, in the same transaction (ADR-0027). A violation of a unique index is returned as an error
/// (see <see cref="UniqueConstraintErrors"/>), and actions registered with <c>IUnitOfWork.OnCommitted</c> (cache invalidation) run after
/// the commit.
/// </para>
/// <para>
/// Only <c>IEntityTypeConfiguration&lt;T&gt;</c> classes from this namespace (<c>Knowledge.Infrastructure.Persistence.Write</c>) are applied.
/// Strongly typed IDs and single-value objects of the domain assembly are converted by convention. Every model change requires a new
/// migration in <c>Migrations/</c> (expand/contract); migrations are applied by <c>SuperApp.Migrator</c>, never at startup.
/// </para>
/// </remarks>
/// <param name="options">Context options from DI registration (write connection string, migrations history table in the service schema).</param>
/// <param name="dispatcher">Domain event dispatcher called from <c>SaveChangesAsync</c>, in the same transaction (ADR-0027).</param>
public sealed class KnowledgeWriteDbContext(DbContextOptions<KnowledgeWriteDbContext> options, IDomainEventDispatcher dispatcher)
    : WriteDbContextBase(options, dispatcher)
{
    /// <summary>
    /// MSSQL schema of the Knowledge service (<c>knowledge</c>): holds its tables, the migrations history and the outbox/inbox tables
    /// (ADR-0021). Also used as the prefix of cache keys and message endpoint names.
    /// </summary>
    public const string SchemaName = "knowledge";

    /// <inheritdoc />
    protected override string Schema => SchemaName;

    /// <inheritdoc />
    protected override Assembly DomainAssembly => KnowledgeDomain.Assembly;

    /// <summary>
    /// Maps the unique indexes a user can violate to the errors of this context, so that <c>IUnitOfWork.SaveChangesAsync</c> returns the
    /// same error for a race as the handlers return for a sequential duplicate, instead of an exception (HTTP 500).
    /// </summary>
    /// <remarks>
    /// <para>The handlers check uniqueness before inserting; two concurrent requests can both pass that check, and then the index decides:</para>
    /// <list type="table">
    ///   <listheader><term>Index</term><description>Error</description></listheader>
    ///   <item><term><c>IX_Categories_Slug</c></term><description><see cref="CategoryErrors.SlugTaken"/> (<c>knowledge.category.slug_taken</c>,
    ///   409), the same error <c>CreateCategoryHandler</c> returns for a slug that is already saved.</description></item>
    ///   <item><term><c>IX_Favorites_UserId_ItemType_ItemId</c></term><description><see cref="LibraryErrors.FavoriteAddedConcurrently"/>
    ///   (<c>knowledge.library.favorite_added_concurrently</c>, 409). A sequential duplicate is an idempotent success, which cannot be
    ///   produced once the save has failed, so the race gets its own conflict error.</description></item>
    ///   <item><term><c>IX_MaterialCompletions_UserId_MaterialId</c></term><description><see cref="LibraryErrors.CompletionRecordedConcurrently"/>
    ///   (<c>knowledge.library.completion_recorded_concurrently</c>, 409), for the same reason as favorites.</description></item>
    /// </list>
    /// <para>
    /// The keys are the index names as they exist in the database (see the migrations); renaming an index requires updating this map.
    /// Any other unique violation, for example a primary key of an owned collection hit by two editors replacing the items or categories
    /// of the same material or collection at the same time, falls back to the generic <c>persistence.duplicate</c> (409) of the framework.
    /// </para>
    /// </remarks>
    protected override IReadOnlyDictionary<string, Error> UniqueConstraintErrors { get; } = new Dictionary<string, Error>
    {
        ["IX_Categories_Slug"] = CategoryErrors.SlugTaken,
        ["IX_Favorites_UserId_ItemType_ItemId"] = LibraryErrors.FavoriteAddedConcurrently,
        ["IX_MaterialCompletions_UserId_MaterialId"] = LibraryErrors.CompletionRecordedConcurrently,
    };
}
