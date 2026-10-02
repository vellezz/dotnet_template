using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Pagination;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using Knowledge.Application;
using Knowledge.Application.Features.Collections.ListCollections;
using Knowledge.Domain.Common;
using Knowledge.Infrastructure.Persistence.Read;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Features.Collections;

/// <summary>
/// Handles <see cref="ListCollections"/>: returns a page of published collections, optionally limited to one category.
/// </summary>
/// <remarks>
/// Only collections in status <c>Published</c> are listed, ordered by publication date descending and then by identifier (a stable order
/// for paging). The page size is clamped to 1..<see cref="Paging.MaxPageSize"/> and the page number to at least 1. The material count
/// is computed in SQL and follows the visibility rules of <see cref="GetCollectionHandler"/>: for an editor (scope
/// <see cref="KnowledgeScopes.CatalogWrite"/>) it counts all items, for anyone else only the items whose material is published, so a
/// reader never learns about draft or archived materials from the count. Not cached.
/// </remarks>
/// <param name="db">Read context of the service.</param>
/// <param name="currentUser">The caller; used to check the editor scope.</param>
internal sealed class ListCollectionsHandler(KnowledgeReadDbContext db, ICurrentUser currentUser)
    : IQueryHandler<ListCollections, Result<PagedResult<CollectionSummaryDto>>>
{
    private static readonly string Published = nameof(PublicationStatus.Published);

    /// <inheritdoc />
    public async Task<Result<PagedResult<CollectionSummaryDto>>> Handle(ListCollections query, CancellationToken cancellationToken)
    {
        var editor = currentUser.HasScope(KnowledgeScopes.CatalogWrite);
        var pageSize = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        var collections = db.Collections.Where(collection => collection.Status == Published);

        if (query.CategoryId is { } categoryId)
        {
            collections = collections.Where(collection =>
                db.CollectionCategories.Any(link => link.CollectionId == collection.Id && link.CategoryId == categoryId));
        }

        var total = await collections.CountAsync(cancellationToken);
        var items = await collections
            .OrderByDescending(collection => collection.PublishedAt)
            .ThenBy(collection => collection.Id)
            .Skip(Paging.Skip(query.Page, pageSize))
            .Take(pageSize)
            .Select(collection => new CollectionSummaryDto(
                collection.Id,
                collection.Title,
                collection.Description,
                db.CollectionItems.Count(item => item.CollectionId == collection.Id
                    && (editor || db.Materials.Any(material => material.Id == item.MaterialId && material.Status == Published))),
                collection.PublishedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<CollectionSummaryDto>(items, Math.Max(query.Page, 1), pageSize, total);
    }
}
