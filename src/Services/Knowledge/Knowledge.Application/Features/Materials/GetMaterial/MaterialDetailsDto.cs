using Knowledge.Application.Content.Blocks;
using Knowledge.Domain.Common;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Materials.GetMaterial;

/// <summary>
/// A material with all its data and content; the result of <see cref="GetMaterial"/>.
/// </summary>
/// <remarks>Read model built from the read database; enums are serialized as strings.</remarks>
/// <param name="Id">Identifier of the material.</param>
/// <param name="Type">Kind of material: <c>Article</c>, <c>Video</c> or <c>Podcast</c>.</param>
/// <param name="Title">Title of the material.</param>
/// <param name="Description">Short description (lead); <see langword="null"/> when there is none.</param>
/// <param name="MainMediaUrl">
/// Absolute <c>https</c> URL of the main recording of a video or podcast; always <see langword="null"/> for an article, and
/// <see langword="null"/> for a draft video or podcast whose media is not set yet.
/// </param>
/// <param name="MainMediaDurationSeconds">Length of the main recording in whole seconds; <see langword="null"/> when unknown or there is no main media.</param>
/// <param name="Status">Publication status: <c>Draft</c>, <c>Published</c> or <c>Archived</c>. Readers only ever see <c>Published</c>.</param>
/// <param name="ReadingTimeMinutes">
/// Estimated reading time of the content in whole minutes, rounded up, at
/// <see cref="Knowledge.Domain.Materials.Content.ContentBuilder.WordsPerMinute"/> words per minute; at least 1 when the content has
/// any words, 0 when it has none. It does not include the length of the main media.
/// </param>
/// <param name="UpdatedAt">Time of the last change of the material (details, content, categories or status).</param>
/// <param name="PublishedAt">Time of the first publication; <see langword="null"/> for a material that was never published.</param>
/// <param name="CategoryIds">Identifiers of the categories the material belongs to, in no particular order; empty when none.</param>
/// <param name="Content">
/// Top-level content blocks in display order, each possibly with nested blocks (see <see cref="ContentBlockDto"/> for the JSON shape);
/// empty for a material without content.
/// </param>
public sealed record MaterialDetailsDto(
    Guid Id,
    MaterialType Type,
    string Title,
    string? Description,
    string? MainMediaUrl,
    int? MainMediaDurationSeconds,
    PublicationStatus Status,
    int ReadingTimeMinutes,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<Guid> CategoryIds,
    IReadOnlyList<ContentBlockDto> Content);
