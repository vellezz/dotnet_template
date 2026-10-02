using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Pagination;
using SuperApp.Framework.Domain.Results;

namespace Knowledge.Application.Features.Collections.ListCollections;

/// <summary>
/// Returns one page of published collections, newest publication first, optionally filtered by category.
/// Requires scope <see cref="KnowledgeScopes.CatalogRead"/>.
/// </summary>
/// <remarks>
/// <para>
/// Only published collections are listed, for readers and editors alike. The material count of each collection depends on the caller,
/// like the material list of <c>GetCollection</c>: a reader counts only published materials, an editor (scope
/// <see cref="KnowledgeScopes.CatalogWrite"/>) counts all items. Ordering: <c>PublishedAt</c> descending, then identifier, so paging is stable. The handler lives in Infrastructure (<c>ListCollectionsHandler</c>, ADR-0026).
/// </para>
/// <para>
/// Paging is lenient instead of validated: a page below 1 is treated as 1 and the page size is clamped to
/// <c>1..</c><see cref="Paging.MaxPageSize"/>; the returned <see cref="PagedResult{T}"/> reports the effective values.
/// </para>
/// <para>
/// Result: a page of <see cref="CollectionSummaryDto"/>, possibly empty. Possible errors: <c>auth.unauthenticated</c> (403),
/// <c>auth.missing_scope</c> (403). An unknown <see cref="CategoryId"/> is not an error; it yields an empty page.
/// </para>
/// </remarks>
/// <param name="CategoryId">Optional filter: only collections assigned to this category; <see langword="null"/> means all categories.</param>
/// <param name="Page">Page number starting at 1; defaults to 1.</param>
/// <param name="PageSize">Requested number of items per page; defaults to 20, clamped to <c>1..</c><see cref="Paging.MaxPageSize"/>.</param>
[RequiresScope(KnowledgeScopes.CatalogRead)]
public sealed record ListCollections(Guid? CategoryId, int Page = 1, int PageSize = 20)
    : IQuery<Result<PagedResult<CollectionSummaryDto>>>;
