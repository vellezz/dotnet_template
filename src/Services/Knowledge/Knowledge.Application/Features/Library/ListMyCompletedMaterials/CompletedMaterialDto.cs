using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Library.ListMyCompletedMaterials;

/// <summary>
/// A material the calling user has marked as completed; one item of the <see cref="ListMyCompletedMaterials"/> result.
/// </summary>
/// <param name="MaterialId">Identifier of the material (always a published one); pass it to <c>GetMaterial</c> for details.</param>
/// <param name="Type">Kind of material: <c>Article</c>, <c>Video</c> or <c>Podcast</c> (<see cref="MaterialType"/>, serialized as a string).</param>
/// <param name="Title">Current title of the material.</param>
/// <param name="CompletedAt">Time when the user marked the material as completed.</param>
public sealed record CompletedMaterialDto(Guid MaterialId, MaterialType Type, string Title, DateTimeOffset CompletedAt);
