using Knowledge.Infrastructure.Persistence.Read.Models;
using System.Reflection;
using SuperApp.Framework.Infrastructure.Persistence;
using Knowledge.Domain;
using Knowledge.Infrastructure.Persistence.Write;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Persistence.Read;

/// <summary>
/// Read context of the Knowledge service: flat read models over the tables of the <c>knowledge</c> schema, used only by query handlers
/// (ADR-0003, ADR-0026).
/// </summary>
/// <remarks>
/// <para>
/// The context maps its own read-model classes (<c>*Row</c>), never domain entities, so queries bypass the domain model and project
/// straight into DTOs. Change tracking is disabled and <c>SaveChanges</c> throws, because this context is read-only; it has no
/// migrations — the schema is owned by <see cref="KnowledgeWriteDbContext"/>.
/// </para>
/// <para>
/// Only <c>IEntityTypeConfiguration&lt;T&gt;</c> classes from this namespace (<c>Knowledge.Infrastructure.Persistence.Read</c>) are
/// applied, so the write-side configurations of the same assembly are not picked up. The sets are <see langword="internal"/>:
/// only query handlers of this assembly use them.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var items = await db.Materials
///     .Where(material =&gt; material.Status == nameof(PublicationStatus.Published))
///     .OrderByDescending(material =&gt; material.PublishedAt)
///     .ToListAsync(cancellationToken);
/// </code>
/// </example>
/// <param name="options">Context options from DI registration (read connection string, no tracking).</param>
public sealed class KnowledgeReadDbContext(DbContextOptions<KnowledgeReadDbContext> options) : ReadDbContextBase(options)
{
    /// <summary>Categories of the catalog.</summary>
    internal IQueryable<CategoryRow> Categories => Set<CategoryRow>();

    /// <summary>Materials in every status; filter by <see cref="MaterialRow.Status"/> for readers.</summary>
    internal IQueryable<MaterialRow> Materials => Set<MaterialRow>();

    /// <summary>Assignments of materials to categories.</summary>
    internal IQueryable<MaterialCategoryRow> MaterialCategories => Set<MaterialCategoryRow>();

    /// <summary>Content blocks of all materials (tree nodes).</summary>
    internal IQueryable<ContentBlockRow> ContentBlocks => Set<ContentBlockRow>();

    /// <summary>Formatted text spans of content blocks.</summary>
    internal IQueryable<ContentTextSpanRow> ContentTextSpans => Set<ContentTextSpanRow>();

    /// <summary>Collections in every status; filter by <see cref="CollectionRow.Status"/> for readers.</summary>
    internal IQueryable<CollectionRow> Collections => Set<CollectionRow>();

    /// <summary>Materials of collections with their positions.</summary>
    internal IQueryable<CollectionItemRow> CollectionItems => Set<CollectionItemRow>();

    /// <summary>Assignments of collections to categories.</summary>
    internal IQueryable<CollectionCategoryRow> CollectionCategories => Set<CollectionCategoryRow>();

    /// <summary>Favorites of all users; always filter by <see cref="FavoriteRow.UserId"/>.</summary>
    internal IQueryable<FavoriteRow> Favorites => Set<FavoriteRow>();

    /// <summary>Completion marks of all users; always filter by <see cref="MaterialCompletionRow.UserId"/>.</summary>
    internal IQueryable<MaterialCompletionRow> MaterialCompletions => Set<MaterialCompletionRow>();

    /// <inheritdoc />
    protected override string Schema => KnowledgeWriteDbContext.SchemaName;

    /// <inheritdoc />
    protected override Assembly DomainAssembly => KnowledgeDomain.Assembly;
}
