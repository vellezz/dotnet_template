using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using Knowledge.Infrastructure.Caching;
using SuperApp.Framework.Infrastructure.Caching;
using Knowledge.Application;
using Knowledge.Application.Features.Materials.GetMaterial;
using Knowledge.Domain.Common;
using Knowledge.Domain.Materials;
using Knowledge.Infrastructure.Persistence.Read;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Features.Materials;

/// <summary>
/// Handles <see cref="GetMaterial"/>: returns one material with its categories and complete content tree.
/// </summary>
/// <remarks>
/// <para>
/// Two paths, chosen by the caller's scope (resource-level authorization in the query handler, ADR-0017):
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b>Editor</b> (scope <see cref="KnowledgeScopes.CatalogWrite"/>): the material in any status is always read from the database,
///   so editors see their changes immediately and drafts never enter the cache.
///   </description></item>
///   <item><description>
///   <b>Reader</b>: only a published material, served from the fail-safe cache (<see cref="KnowledgeCache.MaterialKey"/>,
///   <see cref="KnowledgeCache.PublishedMaterial"/>, ADR-0020) and invalidated by <see cref="MaterialCacheInvalidation"/>. The "not
///   published" result (<see langword="null"/>) is cached as well, which is safe because publishing raises <c>MaterialChanged</c> and
///   removes it.
///   </description></item>
/// </list>
/// <para>
/// A missing or invisible material is reported as <see cref="MaterialErrors.NotFound"/> (<c>knowledge.material.not_found</c>).
/// </para>
/// </remarks>
/// <param name="db">Read context of the service.</param>
/// <param name="cache">Fail-safe cache of the service.</param>
/// <param name="currentUser">The caller; used to check the editor scope.</param>
internal sealed class GetMaterialHandler(KnowledgeReadDbContext db, FailSafeCache cache, ICurrentUser currentUser)
    : IQueryHandler<GetMaterial, Result<MaterialDetailsDto>>
{
    private static readonly string Published = nameof(PublicationStatus.Published);

    /// <inheritdoc />
    public async Task<Result<MaterialDetailsDto>> Handle(GetMaterial query, CancellationToken cancellationToken)
    {
        if (currentUser.HasScope(KnowledgeScopes.CatalogWrite))
        {
            var any = await LoadAsync(query.MaterialId, publishedOnly: false, cancellationToken);
            return any is null ? MaterialErrors.NotFound : any;
        }

        var published = await cache.GetOrCreateAsync(
            KnowledgeCache.MaterialKey(query.MaterialId),
            token => new ValueTask<MaterialDetailsDto?>(LoadAsync(query.MaterialId, publishedOnly: true, token)),
            KnowledgeCache.PublishedMaterial,
            [KnowledgeCache.MaterialTag(query.MaterialId)],
            cancellationToken);

        return published is null ? MaterialErrors.NotFound : published;
    }

    // Loads the material row, its category links and its content tree; null when the material does not exist
    // or, with publishedOnly, is not published.
    private async Task<MaterialDetailsDto?> LoadAsync(Guid materialId, bool publishedOnly, CancellationToken cancellationToken)
    {
        var material = await db.Materials
            .Where(row => row.Id == materialId && (!publishedOnly || row.Status == Published))
            .FirstOrDefaultAsync(cancellationToken);
        if (material is null)
        {
            return null;
        }

        var categoryIds = await db.MaterialCategories
            .Where(row => row.MaterialId == materialId)
            .Select(row => row.CategoryId)
            .ToListAsync(cancellationToken);
        var content = await ContentTreeReader.ReadAsync(db, materialId, cancellationToken);

        return new MaterialDetailsDto(
            material.Id,
            Enum.Parse<MaterialType>(material.Type),
            material.Title,
            material.Description,
            material.MainMediaUrl,
            material.MainMediaDurationSeconds,
            Enum.Parse<PublicationStatus>(material.Status),
            material.ReadingTimeMinutes,
            material.UpdatedAt,
            material.PublishedAt,
            categoryIds,
            content);
    }
}
