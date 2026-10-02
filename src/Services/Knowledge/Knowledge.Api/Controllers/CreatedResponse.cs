namespace Knowledge.Api.Controllers;

/// <summary>Body of the <c>201 Created</c> response returned when a category, material or collection is created.</summary>
/// <remarks>The response has no <c>Location</c> header; build the URL of the new resource from <paramref name="Id"/>.</remarks>
/// <param name="Id">Identifier of the created resource, used in subsequent calls (e.g. <c>/v1/materials/{id}</c>).</param>
public sealed record CreatedResponse(Guid Id);
