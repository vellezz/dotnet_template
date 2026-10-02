namespace Knowledge.Application.Features.Collections.ListCollections;

/// <summary>
/// Short description of a published collection for list screens; one item of the <see cref="ListCollections"/> result.
/// </summary>
/// <param name="Id">Identifier of the collection; pass it to <c>GetCollection</c> for details.</param>
/// <param name="Title">Title of the collection.</param>
/// <param name="Description">Description of the collection; <see langword="null"/> when there is none.</param>
/// <param name="MaterialCount">
/// Number of materials in the collection as the caller sees them in <c>GetCollection</c>: for a reader only published materials, for an
/// editor (scope <c>knowledge.catalog.write</c>) all items regardless of their status.
/// </param>
/// <param name="PublishedAt">Time of the first publication; filled for every item because the list contains only published collections.</param>
public sealed record CollectionSummaryDto(Guid Id, string Title, string? Description, int MaterialCount, DateTimeOffset? PublishedAt);
