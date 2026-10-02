using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using Knowledge.Application.Content;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Materials.ReplaceMaterialContent;

/// <summary>
/// Handles <see cref="ReplaceMaterialContent"/>: loads the material, maps the request blocks to <c>BlockSpec</c> with
/// <see cref="ContentMapper"/> and delegates to <see cref="Material.ReplaceContent"/>.
/// </summary>
/// <remarks>
/// The handler does not validate content; every content rule lives in the aggregate. An identifier that cannot be converted to
/// <see cref="MaterialId"/> is reported as <see cref="MaterialErrors.NotFound"/>.
/// </remarks>
internal sealed class ReplaceMaterialContentHandler(IMaterialRepository materials, IClock clock) : ICommandHandler<ReplaceMaterialContent>
{
    /// <inheritdoc />
    public async Task<Result> Handle(ReplaceMaterialContent command, CancellationToken cancellationToken)
    {
        if (!MaterialId.Create(command.MaterialId).TryGetValue(out var materialId, out _))
        {
            return MaterialErrors.NotFound;
        }

        var material = await materials.GetAsync(materialId, cancellationToken);
        return material is null
            ? MaterialErrors.NotFound
            : material.ReplaceContent(ContentMapper.ToSpecs(command.Blocks), clock.UtcNow);
    }
}
