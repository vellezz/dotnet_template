using SuperApp.Framework.Application.Pagination;
using SuperApp.Framework.Infrastructure.Api;
using Knowledge.Domain.Library.Favorites;
using Knowledge.Application.Features.Library.AddFavorite;
using Knowledge.Application.Features.Library.ListMyCompletedMaterials;
using Knowledge.Application.Features.Library.ListMyFavorites;
using Knowledge.Application.Features.Library.MarkMaterialCompleted;
using Knowledge.Application.Features.Library.RemoveFavorite;
using Knowledge.Application.Features.Library.UnmarkMaterialCompleted;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace Knowledge.Api.Controllers;

/// <summary>
/// The personal library of the calling user: favorite materials and collections, and materials marked as completed (read, watched
/// or listened to).
/// </summary>
/// <remarks>
/// <para>
/// The user is identified by the <c>sub</c> claim of the access token; every operation reads or changes only the caller's own library,
/// there is no way to address another user's library. A token without a <c>sub</c> claim is rejected
/// with <c>403</c> and the code <c>auth.unauthenticated</c>.
/// </para>
/// <para>
/// Every endpoint requires a bearer access token issued for the Knowledge API audience with the scope <c>knowledge.library.read</c>
/// (reading) or <c>knowledge.library.write</c> (changes). Errors are returned as <c>application/problem+json</c> with the extensions
/// <c>code</c> (stable error code) and <c>traceId</c>. All write operations are idempotent, so clients may safely retry them.
/// </para>
/// </remarks>
/// <param name="sender">MediatR sender that forwards commands and queries to the Application layer.</param>
[ApiController]
[Route("v1/me")]
[ProducesErrorResponseType(typeof(void))]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
public sealed class LibraryController(ISender sender) : ControllerBase
{
    /// <summary>Returns a page of the caller's favorites, most recently added first.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.library.read</c>. Only items that are still published are returned: favorites pointing to a material
    /// or collection that has since been archived are hidden (and are removed in the background shortly after archiving). The total
    /// count also counts only visible items.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="page">Page number starting at 1; values lower than 1 are treated as 1.</param>
    /// <param name="pageSize">Page size, 20 by default, clamped to the range 1–100.</param>
    /// <returns>A page of favorites (item type and identifier, current title, date added) with the total count.</returns>
    /// <response code="200">The requested page (possibly empty).</response>
    /// <response code="400">
    /// A query parameter has an invalid format (<c>page</c> or <c>pageSize</c> is not an integer); rejected by the framework with a validation problem with <c>code</c> <c>request.malformed</c> (ADR-0044).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">
    /// The token lacks the scope <c>knowledge.library.read</c> (<c>auth.missing_scope</c>) or has no user identifier
    /// (<c>auth.unauthenticated</c>).
    /// </response>
    [HttpGet("favorites")]
    [ProducesResponseType<PagedResult<FavoriteDto>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    public async Task<IActionResult> Favorites(CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        this.ToActionResult(await sender.Send(new ListMyFavorites(page, pageSize), cancellationToken));

    /// <summary>Adds a published material or collection to the caller's favorites.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.library.write</c>. Only published items can be added. The operation is idempotent: adding an item
    /// that is already a favorite succeeds without changes and keeps the original date added.
    /// </remarks>
    /// <param name="itemType">Kind of the item: <c>Material</c> or <c>Collection</c>.</param>
    /// <param name="itemId">Identifier of the material or collection.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The item is in the favorites.</response>
    /// <response code="400">Invalid item type or an empty identifier (<c>validation.failed</c>).</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">
    /// The token lacks the scope <c>knowledge.library.write</c> (<c>auth.missing_scope</c>) or has no user identifier
    /// (<c>auth.unauthenticated</c>).
    /// </response>
    /// <response code="404">The item does not exist or is not published (<c>knowledge.library.item_not_available</c>).</response>
    /// <response code="409">
    /// Only when two requests add the same item at the same moment: the other request stored it first
    /// (<c>knowledge.library.favorite_added_concurrently</c>). The item is in the favorites; retrying returns 204.
    /// </response>
    [HttpPut("favorites/{itemType}/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> AddFavorite(FavoriteItemType itemType, Guid itemId, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new AddFavorite(itemType, itemId), cancellationToken));

    /// <summary>Removes a material or collection from the caller's favorites.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.library.write</c>. Works for items in any status, including archived ones. The operation is
    /// idempotent: removing an item that is not a favorite succeeds.
    /// </remarks>
    /// <param name="itemType">Kind of the item: <c>Material</c> or <c>Collection</c>.</param>
    /// <param name="itemId">Identifier of the material or collection.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The item is not in the favorites (any more).</response>
    /// <response code="400">Invalid item type or an empty identifier (<c>validation.failed</c>).</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">
    /// The token lacks the scope <c>knowledge.library.write</c> (<c>auth.missing_scope</c>) or has no user identifier
    /// (<c>auth.unauthenticated</c>).
    /// </response>
    [HttpDelete("favorites/{itemType}/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveFavorite(FavoriteItemType itemType, Guid itemId, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new RemoveFavorite(itemType, itemId), cancellationToken));

    /// <summary>Returns a page of materials the caller marked as completed, most recently marked first.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.library.read</c>. Like favorites, only materials that are currently published are listed and counted;
    /// a mark on a material that was archived later is kept but not shown.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="page">Page number starting at 1; values lower than 1 are treated as 1.</param>
    /// <param name="pageSize">Page size, 20 by default, clamped to the range 1–100.</param>
    /// <returns>A page of completed materials (identifier, material type, current title, date marked) with the total count.</returns>
    /// <response code="200">The requested page (possibly empty).</response>
    /// <response code="400">
    /// A query parameter has an invalid format (<c>page</c> or <c>pageSize</c> is not an integer); rejected by the framework with a validation problem with <c>code</c> <c>request.malformed</c> (ADR-0044).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">
    /// The token lacks the scope <c>knowledge.library.read</c> (<c>auth.missing_scope</c>) or has no user identifier
    /// (<c>auth.unauthenticated</c>).
    /// </response>
    [HttpGet("completions")]
    [ProducesResponseType<PagedResult<CompletedMaterialDto>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    public async Task<IActionResult> Completions(CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        this.ToActionResult(await sender.Send(new ListMyCompletedMaterials(page, pageSize), cancellationToken));

    /// <summary>Marks a published material as completed by the caller.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.library.write</c>. Only published materials can be marked. The operation is idempotent: marking
    /// an already completed material succeeds and keeps the original date marked.
    /// </remarks>
    /// <param name="materialId">Identifier of the material.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The material is marked as completed.</response>
    /// <response code="400">Empty identifier (<c>validation.failed</c>).</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">
    /// The token lacks the scope <c>knowledge.library.write</c> (<c>auth.missing_scope</c>) or has no user identifier
    /// (<c>auth.unauthenticated</c>).
    /// </response>
    /// <response code="404">The material does not exist or is not published (<c>knowledge.library.item_not_available</c>).</response>
    /// <response code="409">
    /// Only when two requests mark the same material at the same moment: the other request stored the mark first
    /// (<c>knowledge.library.completion_recorded_concurrently</c>). The material is marked; retrying returns 204.
    /// </response>
    [HttpPut("completions/{materialId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> MarkCompleted(Guid materialId, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new MarkMaterialCompleted(materialId), cancellationToken));

    /// <summary>Removes the caller's completion mark from a material.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.library.write</c>. Works for materials in any status. The operation is idempotent: removing a mark
    /// that does not exist succeeds.
    /// </remarks>
    /// <param name="materialId">Identifier of the material.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The material is not marked as completed (any more).</response>
    /// <response code="400">Empty identifier (<c>validation.failed</c>).</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">
    /// The token lacks the scope <c>knowledge.library.write</c> (<c>auth.missing_scope</c>) or has no user identifier
    /// (<c>auth.unauthenticated</c>).
    /// </response>
    [HttpDelete("completions/{materialId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnmarkCompleted(Guid materialId, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new UnmarkMaterialCompleted(materialId), cancellationToken));
}
