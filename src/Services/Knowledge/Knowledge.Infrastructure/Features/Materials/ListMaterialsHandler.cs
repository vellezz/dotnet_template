using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Pagination;
using SuperApp.Framework.Domain.Results;
using Knowledge.Infrastructure.Persistence.Read.Models;
using Knowledge.Application.Features.Materials.ListMaterials;
using Knowledge.Domain.Common;
using Knowledge.Domain.Materials;
using Knowledge.Infrastructure.Persistence.Read;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Features.Materials;

/// <summary>
/// Handles <see cref="ListMaterials"/>: returns a page of published materials, optionally filtered by category and material type.
/// </summary>
/// <remarks>
/// Only materials in status <c>Published</c> are listed, ordered by publication date descending and then by identifier (a stable order
/// for paging). The type filter compares the enum name stored as a string. The page size is clamped to 1..<see cref="Paging.MaxPageSize"/>
/// and the page number to at least 1. The content is not loaded. Not cached.
/// </remarks>
/// <param name="db">Read context of the service.</param>
internal sealed class ListMaterialsHandler(KnowledgeReadDbContext db) : IQueryHandler<ListMaterials, Result<PagedResult<MaterialSummaryDto>>>
{
    /// <inheritdoc />
    public async Task<Result<PagedResult<MaterialSummaryDto>>> Handle(ListMaterials query, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        var materials = db.Materials.Where(material => material.Status == nameof(PublicationStatus.Published));

        if (query.CategoryId is { } categoryId)
        {
            materials = materials.Where(material => db.MaterialCategories.Any(link => link.MaterialId == material.Id && link.CategoryId == categoryId));
        }

        if (query.Type is { } type)
        {
            var typeName = type.ToString();
            materials = materials.Where(material => material.Type == typeName);
        }

        var total = await materials.CountAsync(cancellationToken);
        var rows = await materials
            .OrderByDescending(material => material.PublishedAt)
            .ThenBy(material => material.Id)
            .Skip(Paging.Skip(query.Page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<MaterialSummaryDto>(rows.Select(ToSummary).ToList(), Math.Max(query.Page, 1), pageSize, total);
    }

    /// <summary>Maps a material row to the summary DTO; shared with <see cref="Collections.GetCollectionHandler"/> for the materials of a collection.</summary>
    /// <param name="row">Material row read from the <c>Materials</c> table.</param>
    /// <returns>The summary of the material.</returns>
    internal static MaterialSummaryDto ToSummary(MaterialRow row) => new(
        row.Id,
        Enum.Parse<MaterialType>(row.Type),
        row.Title,
        row.Description,
        row.ReadingTimeMinutes,
        row.MainMediaDurationSeconds,
        row.PublishedAt);
}
