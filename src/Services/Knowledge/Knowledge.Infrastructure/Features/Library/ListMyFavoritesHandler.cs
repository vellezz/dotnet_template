using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Pagination;
using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Library.Favorites;
using Knowledge.Application.Features.Library.ListMyFavorites;
using Knowledge.Domain.Common;
using Knowledge.Infrastructure.Persistence.Read;
using Microsoft.EntityFrameworkCore;

namespace Knowledge.Infrastructure.Features.Library;

/// <summary>
/// Handles <see cref="ListMyFavorites"/>: returns a page of the current user's favorites that are still published, most recently added
/// first.
/// </summary>
/// <remarks>
/// <para>
/// Library queries are always scoped to the current user's subject (<see cref="ICurrentUser.Subject"/>); without a subject the handler
/// returns <see cref="AuthorizationErrors.Unauthenticated"/> (<c>auth.unauthenticated</c>).
/// </para>
/// <para>
/// A favorite points either to a material or to a collection (<c>ItemType</c> + <c>ItemId</c>, no foreign key). For each favorite the
/// current title is looked up in the matching table, restricted to published items; favorites whose item is missing, a draft or archived
/// get no title and are filtered out in SQL, so both the page and the total count contain only visible items. This hides archived items
/// immediately, before the worker removes their favorites. The page size is clamped to 1..<see cref="Paging.MaxPageSize"/>.
/// Not cached, because the data is per user.
/// </para>
/// </remarks>
/// <param name="db">Read context of the service.</param>
/// <param name="currentUser">The caller whose favorites are listed.</param>
internal sealed class ListMyFavoritesHandler(KnowledgeReadDbContext db, ICurrentUser currentUser)
    : IQueryHandler<ListMyFavorites, Result<PagedResult<FavoriteDto>>>
{
    private static readonly string Published = nameof(PublicationStatus.Published);
    private static readonly string MaterialItem = nameof(FavoriteItemType.Material);

    /// <inheritdoc />
    public async Task<Result<PagedResult<FavoriteDto>>> Handle(ListMyFavorites query, CancellationToken cancellationToken)
    {
        if (currentUser.Subject is not { } subject)
        {
            return AuthorizationErrors.Unauthenticated;
        }

        var pageSize = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        var favorites =
            from favorite in db.Favorites
            where favorite.UserId == subject
            let title = favorite.ItemType == MaterialItem
                ? db.Materials.Where(material => material.Id == favorite.ItemId && material.Status == Published).Select(material => material.Title).FirstOrDefault()
                : db.Collections.Where(collection => collection.Id == favorite.ItemId && collection.Status == Published).Select(collection => collection.Title).FirstOrDefault()
            where title != null
            select new { favorite.ItemType, favorite.ItemId, Title = title, favorite.AddedAt };

        var total = await favorites.CountAsync(cancellationToken);
        var rows = await favorites
            .OrderByDescending(row => row.AddedAt)
            .ThenBy(row => row.ItemType)
            .ThenBy(row => row.ItemId)
            .Skip(Paging.Skip(query.Page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => new FavoriteDto(Enum.Parse<FavoriteItemType>(row.ItemType), row.ItemId, row.Title, row.AddedAt)).ToList();
        return new PagedResult<FavoriteDto>(items, Math.Max(query.Page, 1), pageSize, total);
    }
}
