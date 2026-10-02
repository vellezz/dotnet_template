using FluentValidation;

namespace Knowledge.Application.Features.Materials.ArchiveMaterial;

/// <summary>Input rules of <see cref="ArchiveMaterial"/>: the material identifier must not be empty.</summary>
/// <remarks>Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400).</remarks>
internal sealed class ArchiveMaterialValidator : AbstractValidator<ArchiveMaterial>
{
    /// <summary>Defines the rules.</summary>
    public ArchiveMaterialValidator() => RuleFor(command => command.MaterialId).NotEmpty();
}
