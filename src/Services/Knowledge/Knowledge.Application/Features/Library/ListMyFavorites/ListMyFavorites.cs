using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Pagination;
using SuperApp.Framework.Domain.Results;

namespace Knowledge.Application.Features.Library.ListMyFavorites;

/// <summary>
/// Returns one page of the calling user's favorites (materials and collections together), most recently added first.
/// Requires scope <see cref="KnowledgeScopes.LibraryRead"/>.
/// </summary>
/// <remarks>
/// <para>
/// The user is taken from the token (<c>sub</c>). Only favorites whose item is currently published are returned (and counted in the
/// total); favorites of archived items are removed asynchronously by the Worker, and this filter hides them in the meantime.
/// The handler lives in Infrastructure (<c>ListMyFavoritesHandler</c>, ADR-0026).
/// </para>
/// <para>
/// Paging is lenient instead of validated: a page below 1 is treated as 1 and the page size is clamped to
/// <c>1..</c><see cref="Paging.MaxPageSize"/>.
/// </para>
/// <para>
/// Result: a page of <see cref="FavoriteDto"/>, possibly empty. Possible errors: <c>auth.unauthenticated</c> (403, also when the token
/// has no <c>sub</c> claim), <c>auth.missing_scope</c> (403).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Result&lt;PagedResult&lt;FavoriteDto&gt;&gt; page = await sender.Send(new ListMyFavorites(Page: 2, PageSize: 50), cancellationToken);
/// </code>
/// </example>
/// <param name="Page">Page number starting at 1; defaults to 1.</param>
/// <param name="PageSize">Requested number of items per page; defaults to 20, clamped to <c>1..</c><see cref="Paging.MaxPageSize"/>.</param>
[RequiresScope(KnowledgeScopes.LibraryRead)]
public sealed record ListMyFavorites(int Page = 1, int PageSize = 20) : IQuery<Result<PagedResult<FavoriteDto>>>;
