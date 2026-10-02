using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using Knowledge.Application;
using Knowledge.Application.Features.Collections.GetCollection;
using Knowledge.Domain.Collections;
using Knowledge.Domain.Common;
using Knowledge.Infrastructure.Features.Materials;
using Knowledge.Infrastructure.Persistence.Read;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Features.Collections;

/// <summary>
/// Handles <see cref="GetCollection"/>: returns one collection with its category identifiers and its materials in collection order.
/// </summary>
/// <remarks>
/// <para>
/// Visibility depends on the caller (resource-level authorization in the query handler, ADR-0017): an editor (scope
/// <see cref="KnowledgeScopes.CatalogWrite"/>) sees the collection and its materials in every status; anyone else sees only a published
/// collection and only its published materials. A collection that is not visible is reported as <see cref="CollectionErrors.NotFound"/>
/// (<c>knowledge.collection.not_found</c>), so readers cannot probe for drafts.
/// </para>
/// <para>
/// Runs three queries (collection, materials joined through <c>CollectionItems</c> ordered by position, category links). Not cached.
/// </para>
/// </remarks>
/// <param name="db">Read context of the service.</param>
/// <param name="currentUser">The caller; used to check the editor scope.</param>
internal sealed class GetCollectionHandler(KnowledgeReadDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetCollection, Result<CollectionDetailsDto>>
{
    private static readonly string Published = nameof(PublicationStatus.Published);

    /// <inheritdoc />
    public async Task<Result<CollectionDetailsDto>> Handle(GetCollection query, CancellationToken cancellationToken)
    {
        var editor = currentUser.HasScope(KnowledgeScopes.CatalogWrite);
        var collection = await db.Collections
            .Where(row => row.Id == query.CollectionId && (editor || row.Status == Published))
            .FirstOrDefaultAsync(cancellationToken);
        if (collection is null)
        {
            return CollectionErrors.NotFound;
        }

        var materials = await (
                from item in db.CollectionItems
                join material in db.Materials on item.MaterialId equals material.Id
                where item.CollectionId == collection.Id && (editor || material.Status == Published)
                orderby item.Position
                select material)
            .ToListAsync(cancellationToken);

        var categoryIds = await db.CollectionCategories
            .Where(row => row.CollectionId == collection.Id)
            .Select(row => row.CategoryId)
            .ToListAsync(cancellationToken);

        return new CollectionDetailsDto(
            collection.Id,
            collection.Title,
            collection.Description,
            Enum.Parse<PublicationStatus>(collection.Status),
            collection.PublishedAt,
            categoryIds,
            materials.Select(ListMaterialsHandler.ToSummary).ToList());
    }
}
