using SuperApp.Framework.Application.Messaging;
using SuperApp.Framework.Application.Time;
using SuperApp.Framework.Domain.Results;
using Knowledge.Domain.Materials;

namespace Knowledge.Application.Features.Materials.CreateMaterial;

/// <summary>
/// Handles <see cref="CreateMaterial"/>: creates a draft <see cref="Material"/> with the current time and adds it to the repository.
/// </summary>
/// <remarks>Saving happens in the transaction behavior after the handler returns success.</remarks>
internal sealed class CreateMaterialHandler(IMaterialRepository materials, IClock clock) : ICommandHandler<CreateMaterial, Result<Guid>>
{
    /// <inheritdoc />
    public Task<Result<Guid>> Handle(CreateMaterial command, CancellationToken cancellationToken)
    {
        var created = Material.Create(
            command.Type, command.Title, command.Description, command.MainMediaUrl, command.MainMediaDurationSeconds, clock.UtcNow);
        if (!created.TryGetValue(out var material, out var error))
        {
            return Task.FromResult<Result<Guid>>(error);
        }

        materials.Add(material);
        return Task.FromResult<Result<Guid>>(material.Id.Value);
    }
}
