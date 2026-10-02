using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Pagination;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using Knowledge.Application.Features.Library.ListMyCompletedMaterials;
using Knowledge.Domain.Common;
using Knowledge.Domain.Materials;
using Knowledge.Infrastructure.Persistence.Read;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Features.Library;

/// <summary>
/// Handles <see cref="ListMyCompletedMaterials"/>: returns a page of materials the current user marked as completed, most recent first.
/// </summary>
/// <remarks>
/// Scoped to the current user's subject (<see cref="ICurrentUser.Subject"/>); without a subject it returns
/// <see cref="AuthorizationErrors.Unauthenticated"/> (<c>auth.unauthenticated</c>). Completions are joined with <c>Materials</c> to get the
/// current type and title, restricted to published materials in SQL (like <see cref="ListMyFavoritesHandler"/>), so both the page and the
/// total count contain only visible materials; completions of archived materials stay in the database but are not listed.
/// The page size is clamped to 1..<see cref="Paging.MaxPageSize"/>. Not cached, because the data is per user.
/// </remarks>
/// <param name="db">Read context of the service.</param>
/// <param name="currentUser">The caller whose completions are listed.</param>
internal sealed class ListMyCompletedMaterialsHandler(KnowledgeReadDbContext db, ICurrentUser currentUser)
    : IQueryHandler<ListMyCompletedMaterials, Result<PagedResult<CompletedMaterialDto>>>
{
    private static readonly string Published = nameof(PublicationStatus.Published);

    /// <inheritdoc />
    public async Task<Result<PagedResult<CompletedMaterialDto>>> Handle(ListMyCompletedMaterials query, CancellationToken cancellationToken)
    {
        if (currentUser.Subject is not { } subject)
        {
            return AuthorizationErrors.Unauthenticated;
        }

        var pageSize = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        var completions =
            from completion in db.MaterialCompletions
            join material in db.Materials on completion.MaterialId equals material.Id
            where completion.UserId == subject && material.Status == Published
            select new { completion.MaterialId, material.Type, material.Title, completion.CompletedAt };

        var total = await completions.CountAsync(cancellationToken);
        var rows = await completions
            .OrderByDescending(row => row.CompletedAt)
            .ThenBy(row => row.MaterialId)
            .Skip(Paging.Skip(query.Page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => new CompletedMaterialDto(row.MaterialId, Enum.Parse<MaterialType>(row.Type), row.Title, row.CompletedAt)).ToList();
        return new PagedResult<CompletedMaterialDto>(items, Math.Max(query.Page, 1), pageSize, total);
    }
}
