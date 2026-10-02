namespace SuperApp.Framework.Application.Pagination;

/// <summary>
/// One page of a list query, together with the information a client needs to render paging (total count, page number, page size).
/// </summary>
/// <remarks>
/// <para>
/// Return it from list queries that can grow without bound (materials, collections, favorites), wrapped in
/// <c>Result&lt;PagedResult&lt;TDto&gt;&gt;</c>. The query accepts <c>Page</c> and <c>PageSize</c> with sensible defaults; the handler
/// clamps the page size to <c>1..</c><see cref="Paging.MaxPageSize"/>, computes the offset with <see cref="Paging.Skip"/> and reports
/// the effective values back, so the client always sees what was actually applied.
/// </para>
/// <para>
/// Order the underlying query deterministically (for example by a timestamp, then by ID); otherwise items can repeat or disappear
/// between pages.
/// </para>
/// </remarks>
/// <example>
/// Simplified from <c>ListMyFavoritesHandler</c>:
/// <code>
/// var pageSize = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
/// var favorites = db.Favorites.Where(favorite =&gt; favorite.UserId == subject);
///
/// var total = await favorites.CountAsync(cancellationToken);
/// var items = await favorites
///     .OrderByDescending(favorite =&gt; favorite.AddedAt)
///     .Skip(Paging.Skip(query.Page, pageSize))
///     .Take(pageSize)
///     .Select(favorite =&gt; new FavoriteDto(...))
///     .ToListAsync(cancellationToken);
///
/// return new PagedResult&lt;FavoriteDto&gt;(items, Math.Max(query.Page, 1), pageSize, total);
/// </code>
/// </example>
/// <typeparam name="T">Type of the items, a read DTO with primitive properties.</typeparam>
/// <param name="Items">Items of the requested page, in the query's order; empty when the page is beyond the last item.</param>
/// <param name="Page">Number of the returned page, starting at 1.</param>
/// <param name="PageSize">Effective maximum number of items per page after clamping (1 to <see cref="Paging.MaxPageSize"/>); the last page may contain fewer.</param>
/// <param name="TotalCount">Number of items matching the query across all pages; clients compute the page count as <c>ceil(TotalCount / PageSize)</c>.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
