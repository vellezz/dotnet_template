using SuperApp.Framework.Application.Pagination;
using SuperApp.Framework.Infrastructure.Api;
using Knowledge.Application.Features.Collections.ArchiveCollection;
using Knowledge.Application.Features.Collections.CreateCollection;
using Knowledge.Application.Features.Collections.GetCollection;
using Knowledge.Application.Features.Collections.ListCollections;
using Knowledge.Application.Features.Collections.PublishCollection;
using Knowledge.Application.Features.Collections.SetCollectionCategories;
using Knowledge.Application.Features.Collections.SetCollectionItems;
using Knowledge.Application.Features.Collections.UpdateCollectionDetails;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace Knowledge.Api.Controllers;

/// <summary>
/// Collections: curated, ordered sets of educational materials with a title, a description and categories.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle: a collection is created as <c>Draft</c>, becomes visible to readers when published (<c>Published</c>) and is finally
/// archived (<c>Archived</c>), which is irreversible and freezes it. Readers (scope <c>knowledge.catalog.read</c>) see only published
/// collections; editors (scope <c>knowledge.catalog.write</c>) manage collections in every status.
/// </para>
/// <para>
/// Every endpoint requires a bearer access token issued for the Knowledge API audience; the scope is checked before the request body
/// is validated. Errors are returned as <c>application/problem+json</c> with the extensions <c>code</c> (stable error code) and
/// <c>traceId</c>; validation errors use the code <c>validation.failed</c> and list the invalid fields.
/// </para>
/// </remarks>
/// <param name="sender">MediatR sender that forwards commands and queries to the Application layer.</param>
[ApiController]
[Route("v1/collections")]
[ProducesErrorResponseType(typeof(void))]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
public sealed class CollectionsController(ISender sender) : ControllerBase
{
    /// <summary>Returns a page of published collections, newest first, optionally limited to one category.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.read</c>. Only collections in status <c>Published</c> are returned, ordered by publication
    /// date descending. For readers the material count includes only published materials, the ones they can open; for editors
    /// (scope <c>knowledge.catalog.write</c>) it includes every material of the collection.
    /// </remarks>
    /// <param name="categoryId">Optional filter: only collections assigned to this category. An unknown category yields an empty page.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="page">Page number starting at 1; values lower than 1 are treated as 1.</param>
    /// <param name="pageSize">Page size, 20 by default, clamped to the range 1–100.</param>
    /// <returns>A page of collection summaries (identifier, title, description, material count, publication date) with the total count.</returns>
    /// <response code="200">The requested page (possibly empty).</response>
    /// <response code="400">
    /// A query parameter has an invalid format (<c>categoryId</c> is not a GUID, or <c>page</c> or <c>pageSize</c> is not an integer);
    /// rejected by the framework with a validation problem with <c>code</c> <c>request.malformed</c> (ADR-0044).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.read</c> (<c>auth.missing_scope</c>).</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<CollectionSummaryDto>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? categoryId,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        this.ToActionResult(await sender.Send(new ListCollections(categoryId, page, pageSize), cancellationToken));

    /// <summary>Returns the details of a collection with its materials in the collection's order.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.read</c>. A reader sees only a published collection and only its published materials
    /// (materials that are drafts or archived are left out of the list). An editor (scope <c>knowledge.catalog.write</c>) sees the
    /// collection and all its materials in every status.
    /// </remarks>
    /// <param name="collectionId">Identifier of the collection.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>Collection details: title, description, status, publication date, category identifiers and material summaries.</returns>
    /// <response code="200">The collection details.</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.read</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">
    /// The collection does not exist or is not visible to the caller, e.g. a draft requested by a reader (<c>knowledge.collection.not_found</c>).
    /// </response>
    [HttpGet("{collectionId:guid}")]
    [ProducesResponseType<CollectionDetailsDto>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Get(Guid collectionId, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new GetCollection(collectionId), cancellationToken));

    /// <summary>Creates a new collection in status <c>Draft</c>, without materials and categories.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). Title: required, at most 200 characters; description: optional, at most
    /// 2000 characters (both trimmed; a blank description is stored as none). Add materials with <c>PUT /v1/collections/{id}/items</c>
    /// before publishing.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="command">Title and optional description of the collection.</param>
    /// <returns>The identifier of the created collection.</returns>
    /// <response code="201">The collection was created; the body contains its identifier.</response>
    /// <response code="400">
    /// Invalid title or description (<c>validation.failed</c>, <c>knowledge.collection.invalid_title</c>,
    /// <c>knowledge.collection.invalid_description</c>).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Create(CreateCollection command, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(command, cancellationToken), id => StatusCode(StatusCodes.Status201Created, new CreatedResponse(id)));

    /// <summary>Changes the title and description of a collection.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). Title: required, at most 200 characters; description: optional
    /// (<c>null</c> removes it), at most 2000 characters. Allowed in status <c>Draft</c> and <c>Published</c>; an archived collection
    /// cannot be changed.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="collectionId">Identifier of the collection.</param>
    /// <param name="request">The new title and description.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The details were changed.</response>
    /// <response code="400">
    /// Invalid title, description or an empty identifier (<c>validation.failed</c>, <c>knowledge.collection.invalid_title</c>,
    /// <c>knowledge.collection.invalid_description</c>).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">The collection does not exist (<c>knowledge.collection.not_found</c>).</response>
    /// <response code="422">The collection is archived (<c>knowledge.collection.archived</c>).</response>
    /// <response code="409">
    /// The collection was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpPut("{collectionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> UpdateDetails(Guid collectionId, UpdateCollectionDetailsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new UpdateCollectionDetails(collectionId, request.Title, request.Description), cancellationToken));

    /// <summary>Replaces the list of materials in a collection; the order of the list is the order in the collection.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). At most 200 materials, each at most once; all of them must exist
    /// (in any status: drafts may be added and are hidden from readers until published). A published collection cannot be emptied.
    /// An archived collection cannot be changed.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="collectionId">Identifier of the collection.</param>
    /// <param name="request">The ordered list of material identifiers.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The materials of the collection were set.</response>
    /// <response code="400">
    /// Validation error: missing list or empty identifiers (<c>validation.failed</c>), unknown materials
    /// (<c>knowledge.collection.unknown_materials</c>), too many materials (<c>knowledge.collection.too_many_items</c>)
    /// or duplicates (<c>knowledge.collection.duplicate_items</c>).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">The collection does not exist (<c>knowledge.collection.not_found</c>).</response>
    /// <response code="422">
    /// The collection is archived (<c>knowledge.collection.archived</c>), or it is published and the list is empty
    /// (<c>knowledge.collection.items_required</c>).
    /// </response>
    /// <response code="409">
    /// The collection was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry. Two editors replacing the items at the same moment can also get <c>persistence.duplicate</c>.
    /// </response>
    [HttpPut("{collectionId:guid}/items")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> SetItems(Guid collectionId, SetCollectionItemsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new SetCollectionItems(collectionId, request.MaterialIds), cancellationToken));

    /// <summary>Replaces the set of categories of a collection.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). At most 20 distinct categories, all of which must exist; duplicates in the
    /// request are ignored and an empty list removes all assignments. An archived collection cannot be changed.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="collectionId">Identifier of the collection.</param>
    /// <param name="request">Identifiers of the categories.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The categories of the collection were set.</response>
    /// <response code="400">
    /// Validation error: missing list or empty identifiers (<c>validation.failed</c>), unknown categories (<c>knowledge.category.unknown</c>)
    /// or too many categories (<c>knowledge.collection.too_many_categories</c>).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">The collection does not exist (<c>knowledge.collection.not_found</c>).</response>
    /// <response code="422">The collection is archived (<c>knowledge.collection.archived</c>).</response>
    /// <response code="409">
    /// The collection was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpPut("{collectionId:guid}/categories")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> SetCategories(Guid collectionId, SetCategoriesRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new SetCollectionCategories(collectionId, request.CategoryIds), cancellationToken));

    /// <summary>Publishes a collection, making it visible to readers.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). The collection must contain at least one material. The operation is
    /// idempotent: publishing an already published collection succeeds without changes (the original publication date is kept).
    /// No integration event is published for collections.
    /// </remarks>
    /// <param name="collectionId">Identifier of the collection.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The collection is published.</response>
    /// <response code="400">Empty identifier (<c>validation.failed</c>).</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">The collection does not exist (<c>knowledge.collection.not_found</c>).</response>
    /// <response code="422">
    /// The collection is archived or has no materials (<c>knowledge.collection.archived</c>, <c>knowledge.collection.items_required</c>).
    /// </response>
    /// <response code="409">
    /// The collection was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpPost("{collectionId:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Publish(Guid collectionId, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new PublishCollection(collectionId), cancellationToken));

    /// <summary>Archives a collection: it is hidden from readers and removed from users' favorites.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). Archiving is irreversible (an archived collection can be neither changed
    /// nor published again) and publishes the integration event <c>CollectionArchivedV1</c>. Favorites pointing to the collection are
    /// removed asynchronously, shortly after the response; until then they are already hidden from the favorites list. Materials in the
    /// collection are not affected. The operation is idempotent: archiving an already archived collection succeeds without changes.
    /// </remarks>
    /// <param name="collectionId">Identifier of the collection.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The collection is archived.</response>
    /// <response code="400">Empty identifier (<c>validation.failed</c>).</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">The collection does not exist (<c>knowledge.collection.not_found</c>).</response>
    /// <response code="409">
    /// The collection was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpPost("{collectionId:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Archive(Guid collectionId, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new ArchiveCollection(collectionId), cancellationToken));
}
