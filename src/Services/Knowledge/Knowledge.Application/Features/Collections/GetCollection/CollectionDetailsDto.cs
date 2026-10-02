using Knowledge.Application.Features.Materials.ListMaterials;
using Knowledge.Domain.Common;

namespace Knowledge.Application.Features.Collections.GetCollection;

/// <summary>
/// Details of one collection with its materials in the curated order; the result of <see cref="GetCollection"/>.
/// </summary>
/// <remarks>What is included depends on the caller: see <see cref="GetCollection"/> for the visibility rules.</remarks>
/// <param name="Id">Identifier of the collection.</param>
/// <param name="Title">Title of the collection.</param>
/// <param name="Description">Description of the collection; <see langword="null"/> when there is none.</param>
/// <param name="Status">
/// Publication status (<c>Draft</c>, <c>Published</c> or <c>Archived</c>, serialized as a string). Readers only ever see <c>Published</c>.
/// </param>
/// <param name="PublishedAt">
/// Time of the first publication; <see langword="null"/> for a collection that was never published.
/// </param>
/// <param name="CategoryIds">Identifiers of the categories the collection belongs to, in no particular order; empty when none.</param>
/// <param name="Materials">
/// Materials of the collection in their curated order. Readers get only published materials (draft and archived items are left out);
/// editors get all of them.
/// </param>
public sealed record CollectionDetailsDto(
    Guid Id,
    string Title,
    string? Description,
    PublicationStatus Status,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<Guid> CategoryIds,
    IReadOnlyList<MaterialSummaryDto> Materials);
