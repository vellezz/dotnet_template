using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Materials.UpdateMaterialDetails;

/// <summary>
/// Handles <see cref="UpdateMaterialDetails"/>: loads the material and delegates to <see cref="Material.UpdateDetails"/>.
/// </summary>
/// <remarks>An identifier that cannot be converted to <see cref="MaterialId"/> is reported as <see cref="MaterialErrors.NotFound"/>.</remarks>
internal sealed class UpdateMaterialDetailsHandler(IMaterialRepository materials, IClock clock) : ICommandHandler<UpdateMaterialDetails>
{
    /// <inheritdoc />
    public async Task<Result> Handle(UpdateMaterialDetails command, CancellationToken cancellationToken)
    {
        if (!MaterialId.Create(command.MaterialId).TryGetValue(out var materialId, out _))
        {
            return MaterialErrors.NotFound;
        }

        var material = await materials.GetAsync(materialId, cancellationToken);
        return material is null
            ? MaterialErrors.NotFound
            : material.UpdateDetails(command.Title, command.Description, command.MainMediaUrl, command.MainMediaDurationSeconds, clock.UtcNow);
    }
}
