namespace Knowledge.Api.Controllers;

/// <summary>Request body of <c>PUT /v1/collections/{collectionId}</c>: the new title and description of a collection.</summary>
/// <param name="Title">The new title: required, at most 200 characters (trimmed).</param>
/// <param name="Description">The new description, at most 2000 characters; <see langword="null"/> or a blank string removes it.</param>
public sealed record UpdateCollectionDetailsRequest(string Title, string? Description);
