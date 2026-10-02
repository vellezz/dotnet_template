using SuperApp.Framework.Application.Pagination;
using SuperApp.Framework.Infrastructure.Api;
using Knowledge.Application.Features.Materials.ArchiveMaterial;
using Knowledge.Application.Features.Materials.CreateMaterial;
using Knowledge.Application.Features.Materials.GetMaterial;
using Knowledge.Application.Features.Materials.ListMaterials;
using Knowledge.Application.Features.Materials.PublishMaterial;
using Knowledge.Application.Features.Materials.ReplaceMaterialContent;
using Knowledge.Application.Features.Materials.SetMaterialCategories;
using Knowledge.Application.Features.Materials.UpdateMaterialDetails;
using Knowledge.Domain.Materials;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace Knowledge.Api.Controllers;

/// <summary>
/// Educational materials (articles, videos, podcasts) with block-based content, an optional main media file and categories.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle: a material is created as <c>Draft</c>, filled with content, published (<c>Published</c>) and finally archived
/// (<c>Archived</c>), which is irreversible and freezes it. Readers (scope <c>knowledge.catalog.read</c>) see only published materials;
/// editors (scope <c>knowledge.catalog.write</c>) manage materials in every status.
/// </para>
/// <para>
/// The content is a tree of blocks (headings, paragraphs, lists, callouts, images, tables, transcripts…) sent and returned as JSON,
/// where each block is distinguished by its <c>type</c> field. The content is always replaced as a whole.
/// </para>
/// <para>
/// Every endpoint requires a bearer access token issued for the Knowledge API audience; the scope is checked before the request body
/// is validated. Errors are returned as <c>application/problem+json</c> with the extensions <c>code</c> (stable error code) and
/// <c>traceId</c>; validation errors use the code <c>validation.failed</c> and list the invalid fields.
/// </para>
/// </remarks>
/// <param name="sender">MediatR sender that forwards commands and queries to the Application layer.</param>
[ApiController]
[Route("v1/materials")]
[ProducesErrorResponseType(typeof(void))]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
public sealed class MaterialsController(ISender sender) : ControllerBase
{
    /// <summary>Returns a page of published materials, newest first, optionally filtered by category and type.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.read</c>. Only materials in status <c>Published</c> are returned, ordered by publication
    /// date descending. The summaries do not contain the content; use <c>GET /v1/materials/{id}</c> for it.
    /// </remarks>
    /// <param name="categoryId">Optional filter: only materials assigned to this category. An unknown category yields an empty page.</param>
    /// <param name="type">Optional filter by material type: <c>Article</c>, <c>Video</c> or <c>Podcast</c>.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="page">Page number starting at 1; values lower than 1 are treated as 1.</param>
    /// <param name="pageSize">Page size, 20 by default, clamped to the range 1–100.</param>
    /// <returns>
    /// A page of material summaries (type, title, description, reading time, main media duration, publication date) with the total count.
    /// </returns>
    /// <response code="200">The requested page (possibly empty).</response>
    /// <response code="400">An invalid filter value, e.g. an unknown material type.</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.read</c> (<c>auth.missing_scope</c>).</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<MaterialSummaryDto>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? categoryId,
        [FromQuery] MaterialType? type,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        this.ToActionResult(await sender.Send(new ListMaterials(categoryId, type, page, pageSize), cancellationToken));

    /// <summary>Returns the details of a material with its complete block content.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.read</c>. A reader sees only published materials; an editor
    /// (scope <c>knowledge.catalog.write</c>) also sees drafts and archived materials. Published materials are served from a cache that is
    /// invalidated whenever the material changes, so readers see changes immediately after a successful write.
    /// </remarks>
    /// <param name="materialId">Identifier of the material.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>
    /// Material details: metadata, main media, status, reading time in minutes, last change and publication dates, category identifiers
    /// and the tree of content blocks.
    /// </returns>
    /// <response code="200">The material details.</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.read</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">
    /// The material does not exist or is not visible to the caller, e.g. a draft requested by a reader (<c>knowledge.material.not_found</c>).
    /// </response>
    [HttpGet("{materialId:guid}")]
    [ProducesResponseType<MaterialDetailsDto>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Get(Guid materialId, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new GetMaterial(materialId), cancellationToken));

    /// <summary>Creates a new material in status <c>Draft</c>, without content and categories.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). The type cannot be changed later. Title: required, at most 200 characters;
    /// description: optional, at most 2000 characters (both trimmed). The main media (an absolute <c>https</c> URL of the video or audio
    /// file and its non-negative duration in seconds) applies only to <c>Video</c> and <c>Podcast</c>; an article cannot have it.
    /// A duration without a URL is rejected. Videos and podcasts may be created without main media, but need it to be published.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="command">Type, title, description and optional main media of the material.</param>
    /// <returns>The identifier of the created material.</returns>
    /// <response code="201">The material was created; the body contains its identifier.</response>
    /// <response code="400">
    /// Validation error: invalid type, title, description, URL or duration (<c>validation.failed</c>,
    /// <c>knowledge.material.invalid_title</c>, <c>knowledge.material.invalid_description</c>, <c>knowledge.url.invalid</c>,
    /// <c>knowledge.material.media_not_allowed</c>, <c>knowledge.material.invalid_duration</c>).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Create(CreateMaterial command, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(command, cancellationToken), id => StatusCode(StatusCodes.Status201Created, new CreatedResponse(id)));

    /// <summary>Changes the title, description and main media of a material.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). The same rules as on creation; all fields are replaced, and <c>null</c>
    /// removes the description or the main media. A published video or podcast cannot lose its main media. An archived material cannot
    /// be changed. Changes are not announced to other services.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="materialId">Identifier of the material.</param>
    /// <param name="request">The new details of the material.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The details were changed.</response>
    /// <response code="400">
    /// Validation error: invalid title, description, URL, duration or an empty identifier (<c>validation.failed</c>,
    /// <c>knowledge.material.invalid_title</c>, <c>knowledge.material.invalid_description</c>, <c>knowledge.url.invalid</c>,
    /// <c>knowledge.material.media_not_allowed</c>, <c>knowledge.material.invalid_duration</c>).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">The material does not exist (<c>knowledge.material.not_found</c>).</response>
    /// <response code="422">
    /// The material is archived (<c>knowledge.material.archived</c>), or the main media would be removed from a published video or
    /// podcast (<c>knowledge.material.main_media_required</c>).
    /// </response>
    /// <response code="409">
    /// The material was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpPut("{materialId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> UpdateDetails(Guid materialId, UpdateMaterialDetailsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(
            new UpdateMaterialDetails(materialId, request.Title, request.Description, request.MainMediaUrl, request.MainMediaDurationSeconds),
            cancellationToken));

    /// <summary>Replaces the whole block content of a material.</summary>
    /// <remarks>
    /// <para>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). The content is a tree of blocks distinguished by the <c>type</c> field.
    /// The main rules: at most 500 blocks in total (children included); each block type allows only specific child types; lists can be
    /// nested at most 3 levels deep and toggles 2 levels; every URL must be an absolute <c>https</c> URL (links in text may also use
    /// <c>mailto:</c>); <c>Embed</c> blocks are accepted only from allowed providers; <c>Image</c> blocks require an alternative text.
    /// </para>
    /// <para>
    /// The reading time is recalculated from the new content. A published material cannot be emptied; an archived material cannot be
    /// changed.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="materialId">Identifier of the material.</param>
    /// <param name="request">The new content: top-level blocks with their child blocks.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The content was replaced.</response>
    /// <response code="400">
    /// The content breaks a block rule (<c>knowledge.content.invalid_block</c>; the message names the path of the offending block, e.g.
    /// <c>blocks[2].children[0]</c>), the block list is missing or the identifier is empty (<c>validation.failed</c>), or the body is not
    /// valid content JSON (e.g. an unknown block <c>type</c>).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">The material does not exist (<c>knowledge.material.not_found</c>).</response>
    /// <response code="422">
    /// The material is archived (<c>knowledge.material.archived</c>), or it is published and the new content is empty
    /// (<c>knowledge.material.content_required</c>).
    /// </response>
    /// <response code="409">
    /// The material was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpPut("{materialId:guid}/content")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> ReplaceContent(Guid materialId, ReplaceContentRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new ReplaceMaterialContent(materialId, request.Blocks), cancellationToken));

    /// <summary>Replaces the set of categories of a material.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). At most 20 distinct categories, all of which must exist; duplicates in the
    /// request are ignored and an empty list removes all assignments. An archived material cannot be changed.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <param name="materialId">Identifier of the material.</param>
    /// <param name="request">Identifiers of the categories.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The categories of the material were set.</response>
    /// <response code="400">
    /// Validation error: missing list or empty identifiers (<c>validation.failed</c>), unknown categories (<c>knowledge.category.unknown</c>)
    /// or too many categories (<c>knowledge.material.too_many_categories</c>).
    /// </response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">The material does not exist (<c>knowledge.material.not_found</c>).</response>
    /// <response code="422">The material is archived (<c>knowledge.material.archived</c>).</response>
    /// <response code="409">
    /// The material was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpPut("{materialId:guid}/categories")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> SetCategories(Guid materialId, SetCategoriesRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new SetMaterialCategories(materialId, request.CategoryIds), cancellationToken));

    /// <summary>Publishes a material, making it visible to readers.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). Publishing requires content, and a video or podcast also requires main
    /// media. The first publication records the publication date and publishes the integration event <c>MaterialPublishedV1</c>.
    /// The operation is idempotent: publishing an already published material succeeds without changes and without a new event.
    /// </remarks>
    /// <param name="materialId">Identifier of the material.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The material is published.</response>
    /// <response code="400">Empty identifier (<c>validation.failed</c>).</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">The material does not exist (<c>knowledge.material.not_found</c>).</response>
    /// <response code="422">
    /// The material is archived, has no content or lacks the main media
    /// (<c>knowledge.material.archived</c>, <c>knowledge.material.content_required</c>, <c>knowledge.material.main_media_required</c>).
    /// </response>
    /// <response code="409">
    /// The material was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpPost("{materialId:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Publish(Guid materialId, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new PublishMaterial(materialId), cancellationToken));

    /// <summary>Archives a material: it is hidden from readers and removed from users' favorites.</summary>
    /// <remarks>
    /// Required scope: <c>knowledge.catalog.write</c> (editor). Archiving is irreversible (an archived material can be neither changed nor
    /// published again) and publishes the integration event <c>MaterialArchivedV1</c>. Favorites pointing to the material are removed
    /// asynchronously, shortly after the response; until then they are already hidden from the favorites list. Completion marks are kept.
    /// The material stays in the collections that contain it but is hidden from readers there. The operation is idempotent: archiving
    /// an already archived material succeeds without changes.
    /// </remarks>
    /// <param name="materialId">Identifier of the material.</param>
    /// <param name="cancellationToken">Cancellation of the HTTP request.</param>
    /// <returns>An empty response on success.</returns>
    /// <response code="204">The material is archived.</response>
    /// <response code="400">Empty identifier (<c>validation.failed</c>).</response>
    /// <response code="401">Missing, expired or invalid access token.</response>
    /// <response code="403">The token lacks the scope <c>knowledge.catalog.write</c> (<c>auth.missing_scope</c>).</response>
    /// <response code="404">The material does not exist (<c>knowledge.material.not_found</c>).</response>
    /// <response code="409">
    /// The material was changed by another request between reading and saving (<c>persistence.concurrency_conflict</c>);
    /// reload it and retry.
    /// </response>
    [HttpPost("{materialId:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> Archive(Guid materialId, CancellationToken cancellationToken) =>
        this.ToActionResult(await sender.Send(new ArchiveMaterial(materialId), cancellationToken));
}
