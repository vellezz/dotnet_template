namespace Knowledge.Api.Controllers;

/// <summary>
/// Request body of <c>PUT /v1/collections/{collectionId}/items</c>: the complete new list of materials, replacing the current one.
/// </summary>
/// <param name="MaterialIds">
/// Identifiers of existing materials in the order in which they appear in the collection; at most 200, without duplicates.
/// An empty list is not allowed for a published collection.
/// </param>
public sealed record SetCollectionItemsRequest(IReadOnlyList<Guid> MaterialIds);
