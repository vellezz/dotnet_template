using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Materials.ArchiveMaterial;

/// <summary>
/// Handles <see cref="ArchiveMaterial"/>: loads the material and delegates to <see cref="Material.Archive"/> with the current time.
/// </summary>
/// <remarks>An identifier that cannot be converted to <see cref="MaterialId"/> is reported as <see cref="MaterialErrors.NotFound"/>.</remarks>
internal sealed class ArchiveMaterialHandler(IMaterialRepository materials, IClock clock) : ICommandHandler<ArchiveMaterial>
{
    /// <inheritdoc />
    public async Task<Result> Handle(ArchiveMaterial command, CancellationToken cancellationToken)
    {
        if (!MaterialId.Create(command.MaterialId).TryGetValue(out var materialId, out _))
        {
            return MaterialErrors.NotFound;
        }

        var material = await materials.GetAsync(materialId, cancellationToken);
        return material is null ? MaterialErrors.NotFound : material.Archive(clock.UtcNow);
    }
}
