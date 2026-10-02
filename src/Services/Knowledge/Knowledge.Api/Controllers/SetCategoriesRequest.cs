namespace Knowledge.Api.Controllers;

/// <summary>
/// Request body of <c>PUT /v1/materials/{materialId}/categories</c> and <c>PUT /v1/collections/{collectionId}/categories</c>:
/// the complete new set of categories, replacing the current assignments.
/// </summary>
/// <param name="CategoryIds">
/// Identifiers of existing categories, at most 20 distinct ones; duplicates are ignored and an empty list removes all assignments.
/// </param>
public sealed record SetCategoriesRequest(IReadOnlyList<Guid> CategoryIds);
