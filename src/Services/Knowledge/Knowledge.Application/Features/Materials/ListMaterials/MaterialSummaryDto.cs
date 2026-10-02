using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Materials.ListMaterials;

/// <summary>
/// Short description of a material for list screens: an item of the <see cref="ListMaterials"/> result and of
/// <c>CollectionDetailsDto.Materials</c>.
/// </summary>
/// <remarks>Contains no content; call <c>GetMaterial</c> with <see cref="Id"/> for the full material.</remarks>
/// <param name="Id">Identifier of the material.</param>
/// <param name="Type">Kind of material: <c>Article</c>, <c>Video</c> or <c>Podcast</c> (serialized as a string).</param>
/// <param name="Title">Title of the material.</param>
/// <param name="Description">Short description (lead); <see langword="null"/> when there is none.</param>
/// <param name="ReadingTimeMinutes">
/// Estimated reading time of the content in whole minutes, rounded up; 0 when the content has no words. See
/// <c>MaterialDetailsDto.ReadingTimeMinutes</c>.
/// </param>
/// <param name="MainMediaDurationSeconds">Length of the main recording of a video or podcast in whole seconds; <see langword="null"/> when unknown or not applicable.</param>
/// <param name="PublishedAt">
/// Time of the first publication; <see langword="null"/> for a material that was never published (possible only in an editor's view of
/// a collection).
/// </param>
public sealed record MaterialSummaryDto(
    Guid Id,
    MaterialType Type,
    string Title,
    string? Description,
    int ReadingTimeMinutes,
    int? MainMediaDurationSeconds,
    DateTimeOffset? PublishedAt);
