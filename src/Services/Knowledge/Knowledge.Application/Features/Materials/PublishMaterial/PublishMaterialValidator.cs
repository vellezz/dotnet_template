using FluentValidation;

namespace Knowledge.Application.Features.Materials.PublishMaterial;

/// <summary>Input rules of <see cref="PublishMaterial"/>: the material identifier must not be empty.</summary>
/// <remarks>Runs in the validation behavior before the handler; failures become <c>validation.failed</c> (400).</remarks>
internal sealed class PublishMaterialValidator : AbstractValidator<PublishMaterial>
{
    /// <summary>Defines the rules.</summary>
    public PublishMaterialValidator() => RuleFor(command => command.MaterialId).NotEmpty();
}
