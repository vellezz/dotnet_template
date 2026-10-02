using SuperApp.Framework.Application.Security;
using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Pagination;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Materials.ListMaterials;

/// <summary>
/// Returns one page of published materials, newest publication first, optionally filtered by category and kind.
/// Requires scope <see cref="KnowledgeScopes.CatalogRead"/>.
/// </summary>
/// <remarks>
/// <para>
/// Only published materials are listed, for readers and editors alike; filters are combined with AND. Ordering: <c>PublishedAt</c>
/// descending, then identifier, so paging is stable. The handler lives in Infrastructure (<c>ListMaterialsHandler</c>, ADR-0026).
/// </para>
/// <para>
/// Paging is lenient instead of validated: a page below 1 is treated as 1 and the page size is clamped to
/// <c>1..</c><see cref="Paging.MaxPageSize"/>; the returned <see cref="PagedResult{T}"/> reports the effective values.
/// </para>
/// <para>
/// Result: a page of <see cref="MaterialSummaryDto"/>, possibly empty. Possible errors: <c>auth.unauthenticated</c> (403),
/// <c>auth.missing_scope</c> (403). An unknown <see cref="CategoryId"/> is not an error; it yields an empty page.
/// </para>
/// </remarks>
/// <param name="CategoryId">Optional filter: only materials assigned to this category; <see langword="null"/> means all categories.</param>
/// <param name="Type">Optional filter: only materials of this kind (<c>Article</c>, <c>Video</c>, <c>Podcast</c>); <see langword="null"/> means all kinds.</param>
/// <param name="Page">Page number starting at 1; defaults to 1.</param>
/// <param name="PageSize">Requested number of items per page; defaults to 20, clamped to <c>1..</c><see cref="Paging.MaxPageSize"/>.</param>
[RequiresScope(KnowledgeScopes.CatalogRead)]
public sealed record ListMaterials(Guid? CategoryId, MaterialType? Type, int Page = 1, int PageSize = 20)
    : IQuery<Result<PagedResult<MaterialSummaryDto>>>;
