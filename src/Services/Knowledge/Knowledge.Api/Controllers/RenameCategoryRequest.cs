namespace Knowledge.Api.Controllers;

// Controllers are thin: HTTP <-> command/query through ISender, errors as ProblemDetails (ADR-0015).

/// <summary>Request body of <c>PUT /v1/categories/{categoryId}/name</c>: the new name of a category.</summary>
/// <param name="Name">The new name: required, at most 100 characters (trimmed). The slug of the category does not change.</param>
public sealed record RenameCategoryRequest(string Name);
