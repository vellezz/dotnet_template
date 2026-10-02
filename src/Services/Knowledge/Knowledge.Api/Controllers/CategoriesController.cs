using SuperApp.Framework.Infrastructure.Api;
using Knowledge.Application.Features.Categories.CreateCategory;
using Knowledge.Application.Features.Categories.ListCategories;
using Knowledge.Application.Features.Categories.RenameCategory;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace Knowledge.Api.Controllers;

/// <summary>
/// Categories of the knowledge catalog: a flat (non-hierarchical) list of topics to which materials and collections are assigned.
/// </summary>
/// <remarks>
/// <para>
/// Every endpoint requires a bearer access token issued for the Knowledge API audience; the required scope is listed per endpoint
/// (<c>knowledge.catalog.read</c> for reading, <c>knowledge.catalog.write</c> for editors). The scope is checked before the request body
/// is validated, so a caller without the scope gets <c>403</c> even for an invalid request.
/// </para>
/// <para>
/// Errors are returned as <c>application/problem+json</c> with the extensions <c>code</c> (stable, machine-readable error code such as
/// <c>knowledge.category.slug_taken</c>) and <c>traceId</c> (for support requests). Validation errors use the code
/// <c>validation.failed</c> and list the invalid fields.
/// </para>
/// </remarks>
/// <param name="sender">MediatR sender that forwards commands and queries to the Application layer.</param>
[ApiController]
[Route("v1/categories")]
[ProducesErrorResponseType(typeof(void))]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
public sealed class CategoriesController(ISender sender) : ControllerBase
{
    /// <summary>Returns all categories of the catalog, ordered by name.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.read</c>. The list is not paged (the catalog is expected to have few categories) and is
    /// served from a cache that is refreshed immediately after a category is created or renamed.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>All categories, each with its identifier, display name and slug.</returns>
    /// <response code="200">The list of categories (possibly empty).</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.read</c> (<c>auth.missing_scope</c>).</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new ListCategories(), cancellationToken));

    /// <summary>Creates a new category.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). Name: required, at most 100 characters (leading and trailing whitespace is
    /// trimmed). Slug: required, at most 100 characters, only lowercase letters, digits and single hyphens between them
    /// (e.g. <c>healthy-sleep</c>), unique across all categories. The slug cannot be changed later.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="command">Name and slug of the new category.</param>
    /// <returns>The identifier of the created category.</returns>
    /// <response code="201">The category was created; the body contains its identifier.</response>
    /// <response code="400">
    /// Invalid name or slug (<c>validation.failed</c>, <c>knowledge.category.invalid_name</c>, <c>knowledge.category.invalid_slug</c>).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="409">
    /// A category with this slug already exists (<c>knowledge.category.slug_taken</c>), also when another request created it at the same moment.
    /// </response>
    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Create(CreateCategory command, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(command, cancellationToken), id => StatusCode(StatusCodes.Status201Created, new CreatedResponse(id)));

    /// <summary>Renames a category; its slug stays unchanged.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). Name: required, at most 100 characters (trimmed).
    /// Renaming to the current name succeeds.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="categoryId">Identifier of the category.</param>
    /// <param name="request">The new name of the category.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The category was renamed.</response>
    /// <response code="400">Invalid name or an empty identifier (<c>validation.failed</c>, <c>knowledge.category.invalid_name</c>).</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">The category does not exist (<c>knowledge.category.not_found</c>).</response>
    /// <response code="409">
    /// The category was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpPut("{categoryId:guid}/name")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Rename(Guid categoryId, RenameCategoryRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new RenameCategory(categoryId, request.Name), cancellationToken));
}
